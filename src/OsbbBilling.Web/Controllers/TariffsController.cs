using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class TariffsController(OsbbDbContext database, TariffService tariffService, ILogger<TariffsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? buildingId, int? serviceId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken)) return NotFound();
        if (serviceId.HasValue && !await database.Services.AnyAsync(x => x.ServiceId == serviceId, cancellationToken)) return NotFound();
        var query = database.Tariffs.AsNoTracking();
        if (buildingId.HasValue) query = query.Where(x => x.BuildingId == buildingId);
        if (serviceId.HasValue) query = query.Where(x => x.ServiceId == serviceId);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new TariffListViewModel
        {
            Tariffs = await query.Include(x => x.Building).Include(x => x.Service)
                .OrderBy(x => x.Building.Address).ThenBy(x => x.Service.Name).ThenByDescending(x => x.ValidFrom).ThenBy(x => x.TariffId)
                .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Buildings = await BuildingsAsync(cancellationToken), Services = await ServicesAsync(cancellationToken),
            BuildingId = buildingId, ServiceId = serviceId, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(int? buildingId, int? serviceId, CancellationToken cancellationToken)
    {
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken)) return NotFound();
        if (serviceId.HasValue && !await database.Services.AnyAsync(x => x.ServiceId == serviceId, cancellationToken)) return NotFound();
        var today = DateTime.Today;
        return View(await FillAsync(new TariffFormViewModel
        {
            BuildingId = buildingId ?? 0, ServiceId = serviceId ?? 0, ValidFrom = new DateOnly(today.Year, today.Month, 1)
        }, cancellationToken));
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(TariffFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await tariffService.CreateAsync(new Tariff
                {
                    BuildingId = model.BuildingId, ServiceId = model.ServiceId, UnitPrice = model.UnitPrice,
                    ValidFrom = model.ValidFrom!.Value, ValidTo = model.ValidTo
                }, cancellationToken);
                TempData["Success"] = "Тариф додано.";
                return RedirectToAction(nameof(Index), new { model.BuildingId, model.ServiceId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Tariff could not be saved.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(await FillAsync(model, cancellationToken));
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> NewVersion([FromRoute] int id, CancellationToken cancellationToken)
    {
        var current = await FindAsync(id, cancellationToken);
        if (current is null) return NotFound();
        var next = DateTime.Today.AddMonths(1);
        return View(await FillAsync(new TariffFormViewModel
        {
            CurrentTariff = current, BuildingId = current.BuildingId, ServiceId = current.ServiceId,
            UnitPrice = current.UnitPrice, ValidFrom = new DateOnly(next.Year, next.Month, 1)
        }, cancellationToken));
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> NewVersion([FromRoute] int id, TariffFormViewModel model, CancellationToken cancellationToken)
    {
        var current = await FindAsync(id, cancellationToken);
        if (current is null) return NotFound();
        model.BuildingId = current.BuildingId;
        model.ServiceId = current.ServiceId;
        model.ValidTo = null;
        model.CurrentTariff = current;
        if (ModelState.IsValid)
        {
            try
            {
                var next = await tariffService.NewVersionAsync(id, model.UnitPrice, model.ValidFrom!.Value, cancellationToken);
                if (next is null) return NotFound();
                TempData["Success"] = "Нову версію тарифу додано. Попередню збережено.";
                return RedirectToAction(nameof(Index), new { next.BuildingId, next.ServiceId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "New tariff version could not be saved.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(await FillAsync(model, cancellationToken));
    }

    private Task<Tariff?> FindAsync(int id, CancellationToken cancellationToken) =>
        database.Tariffs.AsNoTracking().Include(x => x.Building).Include(x => x.Service)
            .SingleOrDefaultAsync(x => x.TariffId == id, cancellationToken);
    private async Task<List<SelectListItem>> BuildingsAsync(CancellationToken cancellationToken) =>
        await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
            .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken);
    private async Task<List<SelectListItem>> ServicesAsync(CancellationToken cancellationToken) =>
        await database.Services.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.ServiceId.ToString())).ToListAsync(cancellationToken);
    private async Task<TariffFormViewModel> FillAsync(TariffFormViewModel model, CancellationToken cancellationToken)
    {
        model.Buildings = await BuildingsAsync(cancellationToken);
        model.Services = await ServicesAsync(cancellationToken);
        return model;
    }
}
