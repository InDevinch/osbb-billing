using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class ServicesController(OsbbDbContext database, ILogger<ServicesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        var query = database.Services.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Name.Contains(search) || x.Unit.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new ServiceListViewModel
        {
            Services = await query.OrderBy(x => x.Name).ThenBy(x => x.ServiceId)
                .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Search = search, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public IActionResult Create() => View(new ServiceFormViewModel());

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(ServiceFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        if (await database.Services.AnyAsync(x => x.Name == model.Name.Trim(), cancellationToken))
            ModelState.AddModelError(nameof(model.Name), "Послуга з такою назвою вже є.");
        if (!ModelState.IsValid) return View(model);
        database.Services.Add(new Service { Name = model.Name.Trim(), Unit = model.Unit.Trim(), CalculationMethod = model.CalculationMethod });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
        {
            logger.LogError(exception, "Service could not be saved.");
            ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            return View(model);
        }
        TempData["Success"] = "Послугу додано.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var service = await database.Services.AsNoTracking().SingleOrDefaultAsync(x => x.ServiceId == id, cancellationToken);
        if (service is null) return NotFound();
        return View(new ServiceFormViewModel
        {
            Name = service.Name, Unit = service.Unit, CalculationMethod = service.CalculationMethod,
            HasAssignments = await database.PremiseServices.AnyAsync(x => x.ServiceId == id, cancellationToken)
        });
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, ServiceFormViewModel model, CancellationToken cancellationToken)
    {
        var service = await database.Services.SingleOrDefaultAsync(x => x.ServiceId == id, cancellationToken);
        if (service is null) return NotFound();
        model.HasAssignments = await database.PremiseServices.AnyAsync(x => x.ServiceId == id, cancellationToken);
        if (model.HasAssignments)
        {
            if (model.Unit?.Trim() != service.Unit)
                ModelState.AddModelError(nameof(model.Unit), "Одиницю використаної послуги змінювати не можна.");
            if (model.CalculationMethod != service.CalculationMethod)
                ModelState.AddModelError(nameof(model.CalculationMethod), "Спосіб розрахунку використаної послуги змінювати не можна.");
            model.Unit = service.Unit;
            model.CalculationMethod = service.CalculationMethod;
            ModelState.SetModelValue(nameof(model.Unit), service.Unit, service.Unit);
            ModelState.SetModelValue(nameof(model.CalculationMethod), service.CalculationMethod, service.CalculationMethod);
        }
        if (ModelState.IsValid && await database.Services.AnyAsync(x => x.ServiceId != id && x.Name == model.Name.Trim(), cancellationToken))
            ModelState.AddModelError(nameof(model.Name), "Послуга з такою назвою вже є.");
        if (!ModelState.IsValid) return View(model);
        service.Name = model.Name.Trim();
        service.Unit = model.Unit.Trim();
        service.CalculationMethod = model.CalculationMethod;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
        {
            logger.LogError(exception, "Service could not be saved.");
            ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            return View(model);
        }
        TempData["Success"] = "Послугу збережено.";
        return RedirectToAction(nameof(Index));
    }
}
