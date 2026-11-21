using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Services;

public class AccountService(OsbbDbContext database)
{
    public async Task<Account> OpenAsync(Account account, CancellationToken cancellationToken)
    {
        if (account.ValidFrom.Day != 1)
            throw new AccountRuleException("Оберіть перше число місяця.", nameof(Account.ValidFrom));
        if (account.ValidTo.HasValue && (account.ValidTo.Value.Day != 1 || account.ValidTo <= account.ValidFrom))
            throw new AccountRuleException("Перевірте місяць завершення рахунку.", nameof(Account.ValidTo));
        if (!await database.Premises.AnyAsync(x => x.PremiseId == account.PremiseId, cancellationToken))
            throw new AccountRuleException("Оберіть наявне приміщення.", nameof(Account.PremiseId));
        await ValidateOwnerAndNumberAsync(account.OwnerId, account.AccountNumber, cancellationToken);
        await ValidateIntervalAsync(account, null, cancellationToken);
        database.Accounts.Add(account);
        await database.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<Account?> ChangePayerAsync(int accountId, int ownerId, string number,
        DateOnly validFrom, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var current = await database.Accounts.SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);
        if (current is null) return null;
        if (current.ValidTo.HasValue)
            throw new AccountRuleException("Для цього рахунку вже визначено завершення дії. Відкрийте останній незавершений рахунок приміщення.");
        if (ownerId == current.OwnerId)
            throw new AccountRuleException("Оберіть іншого платника.", nameof(Account.OwnerId));
        if (validFrom.Day != 1 || validFrom <= current.ValidFrom)
            throw new AccountRuleException("Оберіть перше число місяця після початку дії поточного рахунку.", nameof(Account.ValidFrom));
        if (await database.Charges.AnyAsync(x => x.AccountId == accountId && x.Period >= validFrom, cancellationToken))
            throw new AccountRuleException("За цей або пізніший місяць уже є нарахування. Оберіть місяць після останнього нарахування.", nameof(Account.ValidFrom));

        var next = new Account
        {
            AccountNumber = number, OwnerId = ownerId, PremiseId = current.PremiseId, ValidFrom = validFrom
        };
        await ValidateOwnerAndNumberAsync(ownerId, number, cancellationToken);
        await ValidateIntervalAsync(next, accountId, cancellationToken);

        // Спочатку звільняємо інтервал, потім відкриваємо новий рахунок в одній транзакції.
        current.ValidTo = validFrom;
        await database.SaveChangesAsync(cancellationToken);
        database.Accounts.Add(next);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return next;
    }

    private async Task ValidateOwnerAndNumberAsync(int ownerId, string number, CancellationToken cancellationToken)
    {
        if (!await database.Owners.AnyAsync(x => x.OwnerId == ownerId, cancellationToken))
            throw new AccountRuleException("Оберіть наявного платника.", nameof(Account.OwnerId));
        if (await database.Accounts.AnyAsync(x => x.AccountNumber == number, cancellationToken))
            throw new AccountRuleException("Рахунок із таким номером уже є.", nameof(Account.AccountNumber));
    }

    private async Task ValidateIntervalAsync(Account account, int? excludedId, CancellationToken cancellationToken)
    {
        var end = account.ValidTo ?? DateOnly.MaxValue;
        var overlaps = await database.Accounts.AnyAsync(x => x.PremiseId == account.PremiseId
            && (!excludedId.HasValue || x.AccountId != excludedId)
            && x.ValidFrom < end && (!x.ValidTo.HasValue || account.ValidFrom < x.ValidTo), cancellationToken);
        if (overlaps)
            throw new AccountRuleException("Цей період перетинається з іншим рахунком приміщення.", nameof(Account.ValidFrom));
    }
}
