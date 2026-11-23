using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;

namespace OsbbBilling.Web.Services;

public class AccountBalanceService(OsbbDbContext database)
{
    public IQueryable<AccountBalanceViewModel> Query() => database.Accounts.AsNoTracking()
        .Select(account => new
        {
            Account = account,
            Charged = account.Charges.Sum(x => (decimal?)x.Amount) ?? 0,
            Paid = account.Payments.Sum(x => (decimal?)x.Amount) ?? 0,
            Allocated = database.PaymentAllocations.Where(x => x.Payment.AccountId == account.AccountId)
                .Sum(x => (decimal?)x.Amount) ?? 0
        })
        .Select(x => new AccountBalanceViewModel
        {
            AccountId = x.Account.AccountId, AccountNumber = x.Account.AccountNumber,
            OwnerName = x.Account.Owner.FullName, BuildingId = x.Account.Premise.BuildingId,
            Address = x.Account.Premise.Building.Address, PremiseNumber = x.Account.Premise.Number,
            TotalCharged = x.Charged, TotalPaid = x.Paid,
            // Борг і аванс враховують проведені розподіли, а не лише різницю загальних сум.
            DebtAmount = x.Charged - x.Allocated, CreditAmount = x.Paid - x.Allocated,
            NetBalance = x.Charged - x.Paid
        });

    public Task<AccountBalanceViewModel?> FindAsync(int accountId, CancellationToken cancellationToken) =>
        SqlReadRetry.ExecuteAsync(() => Query().SingleOrDefaultAsync(x => x.AccountId == accountId,
            cancellationToken), cancellationToken);
}
