using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

public class WorkspaceController(OsbbDbContext database, FinancialOverviewService overview) : Controller
{
    public IActionResult Index()
    {
        if (User.IsInRole(Roles.Accountant))
            return RedirectToAction(nameof(Accountant));
        if (User.IsInRole(Roles.Chairperson))
            return RedirectToAction(nameof(Chairperson));
        if (User.IsInRole(Roles.Owner))
            return RedirectToAction(nameof(Owner));

        return Forbid();
    }

    [Authorize(Roles = Roles.Accountant)]
    public IActionResult Accountant() => View("Staff");

    [Authorize(Roles = Roles.Chairperson)]
    public async Task<IActionResult> Chairperson(CancellationToken cancellationToken) =>
        View("~/Views/Overview/Index.cshtml", await overview.GetAsync(null, cancellationToken));

    [Authorize(Roles = Roles.Owner)]
    public async Task<IActionResult> Owner(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("OwnerId"), out var ownerId))
            return Forbid();

        // Платник визначається з авторизованого користувача, а не з параметрів запиту.
        var accounts = await database.Accounts.AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.AccountNumber)
            .Select(x => new OwnerAccountViewModel
            {
                AccountId = x.AccountId,
                AccountNumber = x.AccountNumber,
                Address = x.Premise.Building.Address,
                PremiseNumber = x.Premise.Number,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo
            })
            .ToListAsync(cancellationToken);

        return View(accounts);
    }
}
