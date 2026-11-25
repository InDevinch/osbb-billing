using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;

namespace OsbbBilling.Web.Services;

public class FinancialOverviewService(OsbbDbContext database, AccountBalanceService balances)
{
    public async Task<FinancialOverviewViewModel> GetAsync(int? buildingId, CancellationToken cancellationToken)
    {
        var query = balances.Query();
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId);
        var accounts = await SqlReadRetry.ExecuteAsync(() => query.ToListAsync(cancellationToken), cancellationToken);
        return new FinancialOverviewViewModel
        {
            BuildingId = buildingId,
            Buildings = await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
                .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken),
            AccountCount = accounts.Count,
            DebtorCount = accounts.Count(x => x.DebtAmount > 0),
            CreditAccountCount = accounts.Count(x => x.CreditAmount > 0),
            TotalCharged = accounts.Sum(x => x.TotalCharged), TotalPaid = accounts.Sum(x => x.TotalPaid),
            // Аванс іншого рахунку не зменшує загальну суму непогашених боргів.
            TotalDebt = accounts.Sum(x => x.DebtAmount), TotalCredit = accounts.Sum(x => x.CreditAmount)
        };
    }
}
