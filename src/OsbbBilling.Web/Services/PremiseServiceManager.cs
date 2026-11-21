using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Services;

public class PremiseServiceManager(OsbbDbContext database)
{
    public async Task<PremiseService> AssignAsync(PremiseService assignment, CancellationToken cancellationToken)
    {
        if (assignment.ValidFrom.Day != 1 || (assignment.ValidTo.HasValue
            && (assignment.ValidTo.Value.Day != 1 || assignment.ValidTo <= assignment.ValidFrom)))
            throw new CatalogRuleException("Перевірте межі місяців підключення.", nameof(PremiseService.ValidFrom));
        if (!await database.Premises.AnyAsync(x => x.PremiseId == assignment.PremiseId, cancellationToken))
            throw new CatalogRuleException("Оберіть наявне приміщення.", nameof(PremiseService.PremiseId));
        if (!await database.Services.AnyAsync(x => x.ServiceId == assignment.ServiceId, cancellationToken))
            throw new CatalogRuleException("Оберіть наявну послугу.", nameof(PremiseService.ServiceId));
        var end = assignment.ValidTo ?? DateOnly.MaxValue;
        if (await database.PremiseServices.AnyAsync(x => x.PremiseId == assignment.PremiseId && x.ServiceId == assignment.ServiceId
            && x.ValidFrom < end && (!x.ValidTo.HasValue || assignment.ValidFrom < x.ValidTo), cancellationToken))
            throw new CatalogRuleException("Період перетинається з іншим підключенням цієї послуги.", nameof(PremiseService.ValidFrom));
        database.PremiseServices.Add(assignment);
        await database.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task<PremiseService?> StopAsync(int id, DateOnly validTo, CancellationToken cancellationToken)
    {
        var assignment = await database.PremiseServices.SingleOrDefaultAsync(x => x.PremiseServiceId == id, cancellationToken);
        if (assignment is null) return null;
        if (assignment.ValidTo.HasValue)
            throw new CatalogRuleException("Для цього підключення вже визначено завершення.");
        if (validTo.Day != 1 || validTo <= assignment.ValidFrom)
            throw new CatalogRuleException("Оберіть перше число місяця після початку підключення.", nameof(PremiseService.ValidTo));
        if (await database.Charges.AnyAsync(x => x.PremiseServiceId == id && x.Period >= validTo, cancellationToken))
            throw new CatalogRuleException("Обрана дата виключає наявні нарахування.", nameof(PremiseService.ValidTo));
        if (await database.Meters.AnyAsync(x => x.PremiseServiceId == id
            && (!x.RemovedOn.HasValue || x.RemovedOn > validTo), cancellationToken))
            throw new CatalogRuleException("Спочатку завершіть роботу лічильника в межах цього підключення.", nameof(PremiseService.ValidTo));
        assignment.ValidTo = validTo;
        await database.SaveChangesAsync(cancellationToken);
        return assignment;
    }
}
