using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class ChargesController(OsbbDbContext database, ChargeGenerationService generator,
    ILogger<ChargesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? period, int? accountId, int? buildingId,
        string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        if (accountId.HasValue && !await database.Accounts.AnyAsync(x => x.AccountId == accountId, cancellationToken)) return NotFound();
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken)) return NotFound();
        if (period.HasValue && period.Value.Day != 1)
            ModelState.AddModelError(nameof(ChargeListViewModel.Period), "Оберіть перше число місяця.");
        var query = database.Charges.AsNoTracking();
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (period.HasValue) query = query.Where(x => x.Period == period);
        if (accountId.HasValue) query = query.Where(x => x.AccountId == accountId);
        if (buildingId.HasValue) query = query.Where(x => x.Account.Premise.BuildingId == buildingId);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Account.AccountNumber.Contains(search)
                || x.Account.Owner.FullName.Contains(search) || x.PremiseService.Service.Name.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var total = await query.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new ChargeListViewModel
        {
            Charges = await query.Include(x => x.Account).ThenInclude(x => x.Owner)
                .Include(x => x.Account).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
                .Include(x => x.PremiseService).ThenInclude(x => x.Service)
                .OrderByDescending(x => x.Period).ThenBy(x => x.Account.AccountNumber).ThenBy(x => x.ChargeId)
                .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Accounts = await database.Accounts.AsNoTracking().OrderBy(x => x.AccountNumber)
                .Select(x => new SelectListItem(x.AccountNumber, x.AccountId.ToString())).ToListAsync(cancellationToken),
            Buildings = await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
                .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken),
            Period = period, AccountId = accountId, BuildingId = buildingId, Search = search,
            Page = page, TotalPages = totalPages, TotalCount = count, TotalAmount = total
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var charge = await SqlReadRetry.ExecuteAsync(() => database.Charges.AsNoTracking()
            .Include(x => x.Account).ThenInclude(x => x.Owner)
            .Include(x => x.Account).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
            .Include(x => x.PremiseService).ThenInclude(x => x.Service)
            .Include(x => x.CreatedByUser).SingleOrDefaultAsync(x => x.ChargeId == id, cancellationToken), cancellationToken);
        return charge is null ? NotFound() : View(charge);
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public IActionResult Generate()
    {
        var today = DateTime.Today;
        return View(new GenerateChargesViewModel { Period = new DateOnly(today.Year, today.Month, 1) });
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Generate(GenerateChargesViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var createdBy)) return Forbid();
        try
        {
            await generator.GenerateAsync(model.Period!.Value, createdBy, cancellationToken);
            TempData["Success"] = $"Нарахування за {model.Period.Value:MM.yyyy} виконано. Існуючі записи збережено.";
            return RedirectToAction(nameof(Index), new { period = model.Period.Value.ToString("yyyy-MM-dd") });
        }
        catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
        catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
        {
            logger.LogError(exception, "Monthly charges could not be generated.");
            var message = (exception.GetBaseException() as SqlException)?.Number switch
            {
                51030 or 1205 => "Інша фінансова операція ще виконується. Повторіть нарахування.",
                51031 => "Виконувати нарахування може лише активний бухгалтер.",
                51032 => "Оберіть перше число місяця.",
                51033 => "Для активної послуги немає рахунку або тарифу за обраний місяць. Перевірте дані.",
                51034 => "Для послуги за лічильником немає лічильника, поточного або попереднього показання.",
                8115 => "Сума виходить за допустимий діапазон. Перевірте кількість і тариф.",
                _ => "Нарахування не виконано. Операцію скасовано. Перевірте дані та спробуйте ще раз."
            };
            ModelState.AddModelError("", message);
        }
        return View(model);
    }
}
