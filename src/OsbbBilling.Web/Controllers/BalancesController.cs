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
public class BalancesController(OsbbDbContext database, AccountBalanceService balances) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? buildingId, string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken))
            return NotFound();
        var query = balances.Query();
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.AccountNumber.Contains(search) || x.OwnerName.Contains(search)
                || x.Address.Contains(search) || x.PremiseNumber.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new BalanceListViewModel
        {
            Balances = await query.OrderBy(x => x.AccountNumber).ThenBy(x => x.AccountId)
                .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Buildings = await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
                .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken),
            BuildingId = buildingId, Search = search, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }
}
