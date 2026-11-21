using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class PremisesController(OsbbDbContext database) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? buildingId, string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken))
            return NotFound();
        var query = database.Premises.AsNoTracking();
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Number.Contains(search) || x.Building.Address.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        var premises = await query.Include(x => x.Building)
            .OrderBy(x => x.Building.Address).ThenBy(x => x.Number).ThenBy(x => x.PremiseId)
            .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken);
        return View(new PremiseListViewModel
        {
            Premises = premises, Buildings = await BuildingOptionsAsync(cancellationToken),
            BuildingId = buildingId, Search = search, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var premise = await database.Premises.AsNoTracking().Include(x => x.Building)
            .Include(x => x.Accounts).ThenInclude(x => x.Owner)
            .SingleOrDefaultAsync(x => x.PremiseId == id, cancellationToken);
        return premise is null ? NotFound() : View(premise);
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(int? buildingId, CancellationToken cancellationToken)
    {
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken))
            return NotFound();
        return View(new PremiseFormViewModel
        {
            BuildingId = buildingId ?? 0, Buildings = await BuildingOptionsAsync(cancellationToken)
        });
    }

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(PremiseFormViewModel model, CancellationToken cancellationToken)
    {
        await ValidateLocationAsync(model, null, cancellationToken);
        if (!ModelState.IsValid)
            return View(await FillFormAsync(model, false, cancellationToken));
        var premise = new Premise
        {
            BuildingId = model.BuildingId, Number = model.Number.Trim(), Kind = model.Kind,
            Area = model.Area, ResidentsCount = (short)model.ResidentsCount
        };
        database.Premises.Add(premise);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 or 547 })
        {
            ModelState.AddModelError("", "Номер уже зайнятий або будинок більше не доступний. Перевірте дані.");
            return View(await FillFormAsync(model, false, cancellationToken));
        }
        TempData["Success"] = "Приміщення додано.";
        return RedirectToAction(nameof(Details), new { id = premise.PremiseId });
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var premise = await database.Premises.AsNoTracking().SingleOrDefaultAsync(x => x.PremiseId == id, cancellationToken);
        if (premise is null) return NotFound();
        var model = new PremiseFormViewModel
        {
            BuildingId = premise.BuildingId, Number = premise.Number, Kind = premise.Kind,
            Area = premise.Area, ResidentsCount = premise.ResidentsCount
        };
        return View(await FillFormAsync(model, await HasHistoryAsync(id, cancellationToken), cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, PremiseFormViewModel model,
        CancellationToken cancellationToken)
    {
        var premise = await database.Premises.SingleOrDefaultAsync(x => x.PremiseId == id, cancellationToken);
        if (premise is null) return NotFound();
        var hasHistory = await HasHistoryAsync(id, cancellationToken);
        if (hasHistory && premise.BuildingId != model.BuildingId)
        {
            ModelState.AddModelError(nameof(model.BuildingId),
                "Приміщення з рахунками або послугами не можна переносити до іншого будинку.");
            model.BuildingId = premise.BuildingId;
            ModelState.SetModelValue(nameof(model.BuildingId), premise.BuildingId, premise.BuildingId.ToString());
        }
        await ValidateLocationAsync(model, id, cancellationToken);
        if (!ModelState.IsValid)
            return View(await FillFormAsync(model, hasHistory, cancellationToken));
        premise.BuildingId = model.BuildingId;
        premise.Number = model.Number.Trim();
        premise.Kind = model.Kind;
        premise.Area = model.Area;
        premise.ResidentsCount = (short)model.ResidentsCount;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotFound();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 or 547 or 51023 })
        {
            ModelState.AddModelError("", "Зміни суперечать поточним даним будинку або обліковій історії. Перевірте дані.");
            return View(await FillFormAsync(model, await HasHistoryAsync(id, cancellationToken), cancellationToken));
        }
        TempData["Success"] = "Приміщення збережено.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ValidateLocationAsync(PremiseFormViewModel model, int? id, CancellationToken cancellationToken)
    {
        if (!await database.Buildings.AnyAsync(x => x.BuildingId == model.BuildingId, cancellationToken))
            ModelState.AddModelError(nameof(model.BuildingId), "Оберіть наявний будинок.");
        if (!string.IsNullOrWhiteSpace(model.Number))
        {
            var number = model.Number.Trim();
            if (await database.Premises.AnyAsync(x => x.BuildingId == model.BuildingId
                && x.Number == number && (!id.HasValue || x.PremiseId != id), cancellationToken))
                ModelState.AddModelError(nameof(model.Number), "У цьому будинку вже є приміщення з таким номером.");
        }
    }

    private async Task<bool> HasHistoryAsync(int id, CancellationToken cancellationToken) =>
        await database.Accounts.AnyAsync(x => x.PremiseId == id, cancellationToken)
        || await database.PremiseServices.AnyAsync(x => x.PremiseId == id, cancellationToken);

    private async Task<List<SelectListItem>> BuildingOptionsAsync(CancellationToken cancellationToken) =>
        await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
            .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken);

    private async Task<PremiseFormViewModel> FillFormAsync(PremiseFormViewModel model,
        bool hasHistory, CancellationToken cancellationToken)
    {
        model.Buildings = await BuildingOptionsAsync(cancellationToken);
        model.HasHistory = hasHistory;
        return model;
    }
}
