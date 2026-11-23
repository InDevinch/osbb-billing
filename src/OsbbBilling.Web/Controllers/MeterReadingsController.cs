using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant)]
public class MeterReadingsController(OsbbDbContext database, MeterReadingService manager, ILogger<MeterReadingsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(int meterId, CancellationToken cancellationToken)
    {
        var meter = await FindMeterAsync(meterId, cancellationToken);
        if (meter is null) return NotFound();
        var today = DateTime.Today;
        return View(new MeterReadingFormViewModel
        {
            MeterId = meterId, CurrentMeter = meter, Period = new DateOnly(today.Year, today.Month, 1),
            ReadingDate = DateOnly.FromDateTime(today), ReadingValue = meter.InitialReading
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(MeterReadingFormViewModel model, CancellationToken cancellationToken)
    {
        var meter = await FindMeterAsync(model.MeterId, cancellationToken);
        if (meter is null) return NotFound();
        model.CurrentMeter = meter;
        if (ModelState.IsValid)
        {
            try
            {
                await manager.CreateAsync(new MeterReading
                {
                    MeterId = meter.MeterId, Period = model.Period!.Value,
                    ReadingDate = model.ReadingDate!.Value, ReadingValue = model.ReadingValue
                }, cancellationToken);
                TempData["Success"] = "Показання внесено.";
                return RedirectToAction("Details", "Meters", new { id = meter.MeterId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Meter reading could not be saved.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var reading = await SqlReadRetry.ExecuteAsync(() => database.MeterReadings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MeterReadingId == id, cancellationToken), cancellationToken);
        if (reading is null) return NotFound();
        return View(new MeterReadingFormViewModel
        {
            MeterId = reading.MeterId, Period = reading.Period, ReadingDate = reading.ReadingDate,
            ReadingValue = reading.ReadingValue, CurrentReading = reading,
            CurrentMeter = (await FindMeterAsync(reading.MeterId, cancellationToken))!
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit([FromRoute] int id, MeterReadingFormViewModel model, CancellationToken cancellationToken)
    {
        var reading = await SqlReadRetry.ExecuteAsync(() => database.MeterReadings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MeterReadingId == id, cancellationToken), cancellationToken);
        if (reading is null) return NotFound();
        model.MeterId = reading.MeterId;
        model.Period = reading.Period;
        model.CurrentReading = reading;
        model.CurrentMeter = (await FindMeterAsync(reading.MeterId, cancellationToken))!;
        if (ModelState.IsValid)
        {
            try
            {
                if (await manager.EditAsync(id, model.ReadingDate!.Value, model.ReadingValue, cancellationToken) is null) return NotFound();
                TempData["Success"] = "Показання збережено.";
                return RedirectToAction("Details", "Meters", new { id = reading.MeterId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Meter reading could not be edited.");
                ModelState.AddModelError("", CatalogWriteErrors.Message(exception));
            }
        }
        return View(model);
    }

    private Task<Meter?> FindMeterAsync(int id, CancellationToken cancellationToken) =>
        SqlReadRetry.ExecuteAsync(() => database.Meters.AsNoTracking().Include(x => x.PremiseService).ThenInclude(x => x.Service)
            .Include(x => x.PremiseService).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
            .SingleOrDefaultAsync(x => x.MeterId == id, cancellationToken), cancellationToken);
}
