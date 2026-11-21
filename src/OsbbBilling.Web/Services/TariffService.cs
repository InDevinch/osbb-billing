using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Services;

public class TariffService(OsbbDbContext database)
{
    public async Task<Tariff> CreateAsync(Tariff tariff, CancellationToken cancellationToken)
    {
        await ValidateAsync(tariff, null, cancellationToken);
        database.Tariffs.Add(tariff);
        await database.SaveChangesAsync(cancellationToken);
        return tariff;
    }

    public async Task<Tariff?> NewVersionAsync(int id, decimal price, DateOnly validFrom, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var current = await database.Tariffs.SingleOrDefaultAsync(x => x.TariffId == id, cancellationToken);
        if (current is null) return null;
        if (current.ValidTo.HasValue)
            throw new CatalogRuleException("Цю версію вже завершено. Оберіть останній незавершений тариф.");
        if (validFrom <= current.ValidFrom)
            throw new CatalogRuleException("Новий тариф має починатися після початку попереднього.", nameof(Tariff.ValidFrom));
        if (await database.Charges.AnyAsync(x => x.TariffId == id && x.Period >= validFrom, cancellationToken))
            throw new CatalogRuleException("За цей або пізніший місяць уже є нарахування за попереднім тарифом.", nameof(Tariff.ValidFrom));
        var next = new Tariff { BuildingId = current.BuildingId, ServiceId = current.ServiceId, UnitPrice = price, ValidFrom = validFrom };
        await ValidateAsync(next, id, cancellationToken);
        current.ValidTo = validFrom;
        await database.SaveChangesAsync(cancellationToken);
        database.Tariffs.Add(next);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    private async Task ValidateAsync(Tariff tariff, int? excludedId, CancellationToken cancellationToken)
    {
        if (tariff.ValidFrom.Day != 1 || (tariff.ValidTo.HasValue && (tariff.ValidTo.Value.Day != 1 || tariff.ValidTo <= tariff.ValidFrom)))
            throw new CatalogRuleException("Перевірте межі місяців тарифу.", nameof(Tariff.ValidFrom));
        if (tariff.UnitPrice < 0 || tariff.UnitPrice > 99999999999999.9999m || tariff.UnitPrice != decimal.Round(tariff.UnitPrice, 4))
            throw new CatalogRuleException("Перевірте ціну та кількість знаків після коми.", nameof(Tariff.UnitPrice));
        if (!await database.Buildings.AnyAsync(x => x.BuildingId == tariff.BuildingId, cancellationToken))
            throw new CatalogRuleException("Оберіть наявний будинок.", nameof(Tariff.BuildingId));
        if (!await database.Services.AnyAsync(x => x.ServiceId == tariff.ServiceId, cancellationToken))
            throw new CatalogRuleException("Оберіть наявну послугу.", nameof(Tariff.ServiceId));
        var end = tariff.ValidTo ?? DateOnly.MaxValue;
        if (await database.Tariffs.AnyAsync(x => x.BuildingId == tariff.BuildingId && x.ServiceId == tariff.ServiceId
            && (!excludedId.HasValue || x.TariffId != excludedId) && x.ValidFrom < end
            && (!x.ValidTo.HasValue || tariff.ValidFrom < x.ValidTo), cancellationToken))
            throw new CatalogRuleException("Період перетинається з іншим тарифом цього будинку й послуги.", nameof(Tariff.ValidFrom));
    }
}
