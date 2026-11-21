using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class BuildingsController(OsbbDbContext database) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        var query = database.Buildings.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Address.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        var buildings = await query.OrderBy(x => x.Address).ThenBy(x => x.BuildingId)
            .Skip((page - 1) * 20).Take(20)
            .Select(x => new BuildingListItemViewModel
            {
                BuildingId = x.BuildingId, Address = x.Address, PremiseCount = x.Premises.Count
            }).ToListAsync(cancellationToken);
        return View(new BuildingListViewModel
        {
            Buildings = buildings, Search = search, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public IActionResult Create() => View(new BuildingFormViewModel());

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(BuildingFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var address = model.Address.Trim();
        if (await database.Buildings.AnyAsync(x => x.Address == address, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Address), "Будинок із такою адресою вже є.");
            return View(model);
        }
        database.Buildings.Add(new Building { Address = address });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(nameof(model.Address), "Будинок із такою адресою вже є.");
            return View(model);
        }
        TempData["Success"] = "Будинок додано.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var building = await database.Buildings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BuildingId == id, cancellationToken);
        return building is null ? NotFound() : View(new BuildingFormViewModel { Address = building.Address });
    }

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, BuildingFormViewModel model,
        CancellationToken cancellationToken)
    {
        var building = await database.Buildings.SingleOrDefaultAsync(x => x.BuildingId == id, cancellationToken);
        if (building is null) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var address = model.Address.Trim();
        if (await database.Buildings.AnyAsync(x => x.Address == address && x.BuildingId != id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Address), "Будинок із такою адресою вже є.");
            return View(model);
        }
        building.Address = address;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotFound();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(nameof(model.Address), "Будинок із такою адресою вже є.");
            return View(model);
        }
        TempData["Success"] = "Адресу збережено.";
        return RedirectToAction(nameof(Index));
    }
}
