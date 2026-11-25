using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class OverviewController(OsbbDbContext database, FinancialOverviewService overview,
    AccountBalanceService balances) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? buildingId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest("Некоректний фільтр будинку.");
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken))
            return NotFound();
        return View(await overview.GetAsync(buildingId, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Debtors(int? buildingId, string? search, decimal? minimumDebt, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken))
            return NotFound();
        if (minimumDebt.HasValue && (minimumDebt < 0 || minimumDebt > 99999999999999999.99m
            || minimumDebt != decimal.Round(minimumDebt.Value, 2)))
            ModelState.AddModelError(nameof(DebtorListViewModel.MinimumDebt), "Вкажіть невід’ємну суму до 99 999 999 999 999 999,99 грн, не більше двох знаків після коми.");
        var query = balances.Query().Where(x => x.DebtAmount > 0);
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId);
        if (minimumDebt.HasValue) query = query.Where(x => x.DebtAmount >= minimumDebt);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.AccountNumber.Contains(search) || x.OwnerName.Contains(search)
                || x.Address.Contains(search) || x.PremiseNumber.Contains(search));
        var rows = await SqlReadRetry.ExecuteAsync(() => query.OrderByDescending(x => x.DebtAmount)
            .ThenBy(x => x.AccountNumber).ThenBy(x => x.AccountId).ToListAsync(cancellationToken), cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new DebtorListViewModel
        {
            Debtors = rows.Skip((page - 1) * 20).Take(20).ToList(),
            Buildings = await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
                .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken),
            BuildingId = buildingId, Search = search, MinimumDebt = minimumDebt,
            Page = page, TotalPages = totalPages, TotalCount = rows.Count, TotalDebt = rows.Sum(x => x.DebtAmount)
        });
    }
}
