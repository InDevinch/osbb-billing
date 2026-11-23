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
public class PremiseServicesController(OsbbDbContext database, PremiseServiceManager manager,
    ILogger<PremiseServicesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? premiseId, int? serviceId, int page = 1, CancellationToken cancellationToken = default)
    {
        if (premiseId.HasValue && !await database.Premises.AnyAsync(x => x.PremiseId == premiseId, cancellationToken)) return NotFound();
        if (serviceId.HasValue && !await database.Services.AnyAsync(x => x.ServiceId == serviceId, cancellationToken)) return NotFound();
        var query = database.PremiseServices.AsNoTracking();
        if (premiseId.HasValue) query = query.Where(x => x.PremiseId == premiseId);
        if (serviceId.HasValue) query = query.Where(x => x.ServiceId == serviceId);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new PremiseServiceListViewModel
        {
            Assignments = await query.Include(x => x.Service).Include(x => x.Premise).ThenInclude(x => x.Building)
                .OrderBy(x => x.Premise.Building.Address).ThenBy(x => x.Premise.Number).ThenBy(x => x.Service.Name)
                .ThenByDescending(x => x.ValidFrom).ThenBy(x => x.PremiseServiceId)
                .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Premises = await PremisesAsync(cancellationToken), Services = await ServicesAsync(cancellationToken),
            PremiseId = premiseId, ServiceId = serviceId, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(int? premiseId, int? serviceId, CancellationToken cancellationToken)
    {
        if (premiseId.HasValue && !await database.Premises.AnyAsync(x => x.PremiseId == premiseId, cancellationToken)) return NotFound();
        if (serviceId.HasValue && !await database.Services.AnyAsync(x => x.ServiceId == serviceId, cancellationToken)) return NotFound();
        var today = DateTime.Today;
        return View(await FillAsync(new PremiseServiceFormViewModel
        {
            PremiseId = premiseId ?? 0, ServiceId = serviceId ?? 0, ValidFrom = new DateOnly(today.Year, today.Month, 1)
        }, cancellationToken));
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(PremiseServiceFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var assignment = await manager.AssignAsync(new PremiseService
                {
                    PremiseId = model.PremiseId, ServiceId = model.ServiceId,
                    ValidFrom = model.ValidFrom!.Value, ValidTo = model.ValidTo
                }, cancellationToken);
                TempData["Success"] = "Послугу підключено.";
                return RedirectToAction(nameof(Index), new { assignment.PremiseId, assignment.ServiceId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Service assignment could not be saved.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(await FillAsync(model, cancellationToken));
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Stop([FromRoute] int id, CancellationToken cancellationToken)
    {
        var assignment = await FindAsync(id, cancellationToken);
        if (assignment is null) return NotFound();
        var next = DateTime.Today.AddMonths(1);
        return View(new StopPremiseServiceViewModel
        {
            CurrentAssignment = assignment, ValidTo = new DateOnly(next.Year, next.Month, 1)
        });
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Stop([FromRoute] int id, StopPremiseServiceViewModel model, CancellationToken cancellationToken)
    {
        var assignment = await FindAsync(id, cancellationToken);
        if (assignment is null) return NotFound();
        model.CurrentAssignment = assignment;
        if (ModelState.IsValid)
        {
            try
            {
                var stopped = await manager.StopAsync(id, model.ValidTo!.Value, cancellationToken);
                if (stopped is null) return NotFound();
                TempData["Success"] = "Завершення послуги збережено.";
                return RedirectToAction(nameof(Index), new { stopped.PremiseId, stopped.ServiceId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Service assignment could not be stopped.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(model);
    }

    private Task<PremiseService?> FindAsync(int id, CancellationToken cancellationToken) =>
        SqlReadRetry.ExecuteAsync(() => database.PremiseServices.AsNoTracking().Include(x => x.Service).Include(x => x.Premise).ThenInclude(x => x.Building)
            .SingleOrDefaultAsync(x => x.PremiseServiceId == id, cancellationToken), cancellationToken);
    private async Task<List<SelectListItem>> PremisesAsync(CancellationToken cancellationToken) =>
        await database.Premises.AsNoTracking().OrderBy(x => x.Building.Address).ThenBy(x => x.Number)
            .Select(x => new SelectListItem(x.Building.Address + " / " + x.Number, x.PremiseId.ToString())).ToListAsync(cancellationToken);
    private async Task<List<SelectListItem>> ServicesAsync(CancellationToken cancellationToken) =>
        await database.Services.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.ServiceId.ToString())).ToListAsync(cancellationToken);
    private async Task<PremiseServiceFormViewModel> FillAsync(PremiseServiceFormViewModel model, CancellationToken cancellationToken)
    {
        model.Premises = await PremisesAsync(cancellationToken);
        model.Services = await ServicesAsync(cancellationToken);
        return model;
    }
}
