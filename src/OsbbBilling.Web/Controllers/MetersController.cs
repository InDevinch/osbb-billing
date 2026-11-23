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
public class MetersController(OsbbDbContext database, MeterService manager, ILogger<MetersController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? premiseServiceId, string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        if (premiseServiceId.HasValue && !await database.PremiseServices.AnyAsync(x => x.PremiseServiceId == premiseServiceId, cancellationToken))
            return NotFound();
        var query = database.Meters.AsNoTracking();
        if (premiseServiceId.HasValue) query = query.Where(x => x.PremiseServiceId == premiseServiceId);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.SerialNumber.Contains(search) || x.PremiseService.Premise.Building.Address.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new MeterListViewModel
        {
            Meters = await query.Include(x => x.PremiseService).ThenInclude(x => x.Service)
                .Include(x => x.PremiseService).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
                .OrderBy(x => x.SerialNumber).ThenBy(x => x.MeterId).Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Assignments = await OptionsAsync(cancellationToken), PremiseServiceId = premiseServiceId,
            Search = search, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var meter = await FindAsync(id, cancellationToken);
        if (meter is null) return NotFound();
        var readings = await database.MeterReadings.AsNoTracking().Where(x => x.MeterId == id).OrderBy(x => x.Period).ToListAsync(cancellationToken);
        var values = readings.ToDictionary(x => x.Period, x => x.ReadingValue);
        var chargedPeriods = (await database.Charges.Where(x => x.PremiseServiceId == meter.PremiseServiceId)
            .Select(x => x.Period).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var rows = readings.Select(reading =>
        {
            decimal? previous = reading.Period == meter.InstalledOn ? meter.InitialReading : null;
            if (reading.Period > meter.InstalledOn && values.TryGetValue(reading.Period.AddMonths(-1), out var prior))
                previous = prior;
            var next = reading.Period.Year == 9999 && reading.Period.Month == 12 ? (DateOnly?)null : reading.Period.AddMonths(1);
            return new MeterReadingRowViewModel
            {
                MeterReadingId = reading.MeterReadingId, Period = reading.Period, ReadingDate = reading.ReadingDate,
                ReadingValue = reading.ReadingValue, Consumption = previous.HasValue ? reading.ReadingValue - previous.Value : null,
                IsUsed = chargedPeriods.Contains(reading.Period) || next.HasValue && chargedPeriods.Contains(next.Value)
            };
        }).Reverse().ToList();
        return View(new MeterDetailsViewModel { Meter = meter, Readings = rows });
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(int? premiseServiceId, CancellationToken cancellationToken)
    {
        if (premiseServiceId.HasValue && !await database.PremiseServices.AnyAsync(x => x.PremiseServiceId == premiseServiceId, cancellationToken))
            return NotFound();
        var today = DateTime.Today;
        return View(new InstallMeterViewModel
        {
            PremiseServiceId = premiseServiceId ?? 0, InstalledOn = new DateOnly(today.Year, today.Month, 1),
            Assignments = await OptionsAsync(cancellationToken)
        });
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(InstallMeterViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var meter = await manager.InstallAsync(new Meter
                {
                    PremiseServiceId = model.PremiseServiceId, SerialNumber = model.SerialNumber.Trim(),
                    InstalledOn = model.InstalledOn!.Value, RemovedOn = model.RemovedOn, InitialReading = model.InitialReading
                }, cancellationToken);
                TempData["Success"] = "Лічильник встановлено.";
                return RedirectToAction(nameof(Details), new { id = meter.MeterId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Meter could not be installed.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        model.Assignments = await OptionsAsync(cancellationToken);
        return View(model);
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Remove([FromRoute] int id, CancellationToken cancellationToken)
    {
        var meter = await FindAsync(id, cancellationToken);
        if (meter is null) return NotFound();
        var next = DateTime.Today.AddMonths(1);
        return View(new RemoveMeterViewModel { CurrentMeter = meter, RemovedOn = new DateOnly(next.Year, next.Month, 1) });
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Remove([FromRoute] int id, RemoveMeterViewModel model, CancellationToken cancellationToken)
    {
        var meter = await FindAsync(id, cancellationToken);
        if (meter is null) return NotFound();
        model.CurrentMeter = meter;
        if (ModelState.IsValid)
        {
            try
            {
                if (await manager.RemoveAsync(id, model.RemovedOn!.Value, cancellationToken) is null) return NotFound();
                TempData["Success"] = "Завершення роботи лічильника збережено.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Meter could not be removed.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(model);
    }

    private Task<Meter?> FindAsync(int id, CancellationToken cancellationToken) =>
        SqlReadRetry.ExecuteAsync(() => database.Meters.AsNoTracking().Include(x => x.PremiseService).ThenInclude(x => x.Service)
            .Include(x => x.PremiseService).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
            .SingleOrDefaultAsync(x => x.MeterId == id, cancellationToken), cancellationToken);

    private async Task<List<SelectListItem>> OptionsAsync(CancellationToken cancellationToken) =>
        await database.PremiseServices.AsNoTracking().Where(x => x.Service.CalculationMethod == "Meter")
            .OrderBy(x => x.Premise.Building.Address).ThenBy(x => x.Premise.Number).ThenBy(x => x.Service.Name)
            .Select(x => new SelectListItem(x.Premise.Building.Address + " / " + x.Premise.Number + " — " + x.Service.Name,
                x.PremiseServiceId.ToString())).ToListAsync(cancellationToken);
}
