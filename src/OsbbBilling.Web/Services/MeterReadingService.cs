using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Services;

public class MeterReadingService(OsbbDbContext database)
{
    public async Task<MeterReading> CreateAsync(MeterReading reading, CancellationToken cancellationToken)
    {
        var meter = await database.Meters.AsNoTracking().SingleOrDefaultAsync(x => x.MeterId == reading.MeterId, cancellationToken);
        if (meter is null) throw new CatalogRuleException("Лічильник не знайдено.");
        if (await database.MeterReadings.AnyAsync(x => x.MeterId == meter.MeterId && x.Period == reading.Period, cancellationToken))
            throw new CatalogRuleException("За цей місяць показання вже внесено.", nameof(MeterReading.Period));
        await EnsureNotUsedAsync(meter.PremiseServiceId, reading.Period, cancellationToken);
        await ValidateAsync(meter, reading.Period, reading.ReadingDate, reading.ReadingValue, cancellationToken);
        database.MeterReadings.Add(reading);
        await database.SaveChangesAsync(cancellationToken);
        return reading;
    }

    public async Task<MeterReading?> EditAsync(int id, DateOnly readingDate, decimal value, CancellationToken cancellationToken)
    {
        var reading = await database.MeterReadings.Include(x => x.Meter).SingleOrDefaultAsync(x => x.MeterReadingId == id, cancellationToken);
        if (reading is null) return null;
        await EnsureNotUsedAsync(reading.Meter.PremiseServiceId, reading.Period, cancellationToken);
        await ValidateAsync(reading.Meter, reading.Period, readingDate, value, cancellationToken);
        reading.ReadingDate = readingDate;
        reading.ReadingValue = value;
        await database.SaveChangesAsync(cancellationToken);
        return reading;
    }

    private async Task EnsureNotUsedAsync(int assignmentId, DateOnly period, CancellationToken cancellationToken)
    {
        var next = period.Year == 9999 && period.Month == 12 ? (DateOnly?)null : period.AddMonths(1);
        if (await database.Charges.AnyAsync(x => x.PremiseServiceId == assignmentId
            && (x.Period == period || next.HasValue && x.Period == next), cancellationToken))
            throw new CatalogRuleException("Показання вже використане в нарахуванні за цей або наступний місяць. Змінювати його не можна.");
    }

    private async Task ValidateAsync(Meter meter, DateOnly period, DateOnly date, decimal value, CancellationToken cancellationToken)
    {
        if (period.Day != 1 || period < meter.InstalledOn || meter.RemovedOn.HasValue && period >= meter.RemovedOn)
            throw new CatalogRuleException("Місяць показання має входити в період роботи лічильника.", nameof(MeterReading.Period));
        if (date.Year != period.Year || date.Month != period.Month)
            throw new CatalogRuleException("Дата зняття має бути в обраному місяці.", nameof(MeterReading.ReadingDate));
        if (value < 0 || value > 999999999999999.999m || value != decimal.Round(value, 3))
            throw new CatalogRuleException("Перевірте показання та кількість знаків після коми.", nameof(MeterReading.ReadingValue));
        decimal previous = meter.InitialReading;
        if (period != meter.InstalledOn)
        {
            var previousPeriod = period.AddMonths(-1);
            var previousReading = await database.MeterReadings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.MeterId == meter.MeterId && x.Period == previousPeriod, cancellationToken);
            if (previousReading is null)
                throw new CatalogRuleException("Спочатку внесіть показання за попередній місяць.", nameof(MeterReading.Period));
            previous = previousReading.ReadingValue;
        }
        if (value < meter.InitialReading || value < previous)
            throw new CatalogRuleException("Показання не може бути меншим за початкове або попереднє.", nameof(MeterReading.ReadingValue));
        var next = await database.MeterReadings.AsNoTracking().Where(x => x.MeterId == meter.MeterId && x.Period > period)
            .OrderBy(x => x.Period).Select(x => (decimal?)x.ReadingValue).FirstOrDefaultAsync(cancellationToken);
        if (next.HasValue && value > next)
            throw new CatalogRuleException("Показання не може перевищувати наступне внесене показання.", nameof(MeterReading.ReadingValue));
    }
}
