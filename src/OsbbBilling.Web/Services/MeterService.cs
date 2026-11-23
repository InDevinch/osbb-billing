using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Services;

public class MeterService(OsbbDbContext database)
{
    public async Task<Meter> InstallAsync(Meter meter, CancellationToken cancellationToken)
    {
        var assignment = await database.PremiseServices.AsNoTracking().Include(x => x.Service)
            .SingleOrDefaultAsync(x => x.PremiseServiceId == meter.PremiseServiceId, cancellationToken);
        if (assignment is null || assignment.Service.CalculationMethod != "Meter")
            throw new CatalogRuleException("Оберіть підключення послуги з розрахунком за лічильником.", nameof(Meter.PremiseServiceId));
        if (meter.InstalledOn.Day != 1 || meter.InstalledOn < assignment.ValidFrom)
            throw new CatalogRuleException("Встановлення має починатися з першого числа в межах підключення послуги.", nameof(Meter.InstalledOn));
        if (meter.RemovedOn.HasValue && (meter.RemovedOn.Value.Day != 1 || meter.RemovedOn <= meter.InstalledOn))
            throw new CatalogRuleException("Перевірте місяць завершення роботи.", nameof(Meter.RemovedOn));
        if (assignment.ValidTo.HasValue && (!meter.RemovedOn.HasValue || meter.RemovedOn > assignment.ValidTo))
            throw new CatalogRuleException("Робота лічильника не може виходити за період підключення послуги.", nameof(Meter.RemovedOn));
        if (meter.InitialReading < 0 || meter.InitialReading > 999999999999999.999m
            || meter.InitialReading != decimal.Round(meter.InitialReading, 3))
            throw new CatalogRuleException("Перевірте початкове показання.", nameof(Meter.InitialReading));
        if (await database.Meters.AnyAsync(x => x.SerialNumber == meter.SerialNumber, cancellationToken))
            throw new CatalogRuleException("Лічильник із таким серійним номером уже є.", nameof(Meter.SerialNumber));
        var end = meter.RemovedOn ?? DateOnly.MaxValue;
        if (await database.Meters.AnyAsync(x => x.PremiseServiceId == meter.PremiseServiceId
            && x.InstalledOn < end && (!x.RemovedOn.HasValue || meter.InstalledOn < x.RemovedOn), cancellationToken))
            throw new CatalogRuleException("На цей період для послуги вже є інший лічильник.", nameof(Meter.InstalledOn));
        database.Meters.Add(meter);
        await database.SaveChangesAsync(cancellationToken);
        return meter;
    }

    public async Task<Meter?> RemoveAsync(int id, DateOnly removedOn, CancellationToken cancellationToken)
    {
        var meter = await database.Meters.Include(x => x.PremiseService)
            .SingleOrDefaultAsync(x => x.MeterId == id, cancellationToken);
        if (meter is null) return null;
        if (meter.RemovedOn.HasValue)
            throw new CatalogRuleException("Для лічильника вже визначено завершення роботи.");
        if (removedOn.Day != 1 || removedOn <= meter.InstalledOn)
            throw new CatalogRuleException("Оберіть перше число місяця після встановлення.", nameof(Meter.RemovedOn));
        if (meter.PremiseService.ValidTo.HasValue && removedOn > meter.PremiseService.ValidTo)
            throw new CatalogRuleException("Дата виходить за період підключення послуги.", nameof(Meter.RemovedOn));
        if (await database.MeterReadings.AnyAsync(x => x.MeterId == id && x.Period >= removedOn, cancellationToken))
            throw new CatalogRuleException("Обрана дата виключає вже внесені показання.", nameof(Meter.RemovedOn));
        if (await database.Charges.AnyAsync(x => x.PremiseServiceId == meter.PremiseServiceId && x.Period >= removedOn, cancellationToken))
            throw new CatalogRuleException("Обрана дата виключає вже проведені нарахування.", nameof(Meter.RemovedOn));
        meter.RemovedOn = removedOn;
        await database.SaveChangesAsync(cancellationToken);
        return meter;
    }
}
