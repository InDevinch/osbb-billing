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
public class PaymentsController(OsbbDbContext database, PaymentRegistrationService registration,
    ILogger<PaymentsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? dateFrom, DateOnly? dateTo, int? accountId, int? buildingId,
        string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        if (accountId.HasValue && !await database.Accounts.AnyAsync(x => x.AccountId == accountId, cancellationToken)) return NotFound();
        if (buildingId.HasValue && !await database.Buildings.AnyAsync(x => x.BuildingId == buildingId, cancellationToken)) return NotFound();
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
            ModelState.AddModelError(nameof(PaymentListViewModel.DateTo), "Дата завершення має бути не раніше початку.");
        var query = database.Payments.AsNoTracking();
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (dateFrom.HasValue) query = query.Where(x => x.PaymentDate >= dateFrom);
        if (dateTo.HasValue) query = query.Where(x => x.PaymentDate <= dateTo);
        if (accountId.HasValue) query = query.Where(x => x.AccountId == accountId);
        if (buildingId.HasValue) query = query.Where(x => x.Account.Premise.BuildingId == buildingId);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Reference.Contains(search) || x.Account.AccountNumber.Contains(search)
                || x.Account.Owner.FullName.Contains(search));
        var count = await query.CountAsync(cancellationToken);
        var total = await query.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new PaymentListViewModel
        {
            Payments = await query.Include(x => x.Account).ThenInclude(x => x.Owner)
                .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.PaymentId)
                .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken),
            Accounts = await AccountOptionsAsync(cancellationToken),
            Buildings = await database.Buildings.AsNoTracking().OrderBy(x => x.Address)
                .Select(x => new SelectListItem(x.Address, x.BuildingId.ToString())).ToListAsync(cancellationToken),
            DateFrom = dateFrom, DateTo = dateTo, AccountId = accountId, BuildingId = buildingId, Search = search,
            Page = page, TotalPages = totalPages, TotalCount = count, TotalAmount = total
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var payment = await SqlReadRetry.ExecuteAsync(() => database.Payments.AsNoTracking()
            .Include(x => x.Account).ThenInclude(x => x.Owner)
            .Include(x => x.Account).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
            .Include(x => x.CreatedByUser)
            .Include(x => x.PaymentAllocations).ThenInclude(x => x.Charge).ThenInclude(x => x.PremiseService).ThenInclude(x => x.Service)
            .SingleOrDefaultAsync(x => x.PaymentId == id, cancellationToken), cancellationToken);
        return payment is null ? NotFound() : View(payment);
    }

    [HttpGet, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(int? accountId, CancellationToken cancellationToken)
    {
        if (accountId.HasValue && !await database.Accounts.AnyAsync(x => x.AccountId == accountId, cancellationToken)) return NotFound();
        return View(await FillAsync(new PaymentFormViewModel
        {
            AccountId = accountId ?? 0, PaymentDate = DateOnly.FromDateTime(DateTime.Today)
        }, cancellationToken));
    }

    [HttpPost, Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(PaymentFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var createdBy)) return Forbid();
            try
            {
                await registration.RegisterAsync(model.AccountId, model.PaymentDate!.Value, model.Amount,
                    model.Reference, createdBy, cancellationToken);
                TempData["Success"] = "Оплату зареєстровано. Суму розподілено на борг рахунку, залишок збережено як аванс.";
                return RedirectToAction(nameof(Index), new { model.AccountId });
            }
            catch (CatalogRuleException exception) { ModelState.AddModelError(exception.Field, exception.Message); }
            catch (Exception exception) when (CatalogWriteErrors.IsDatabaseError(exception))
            {
                logger.LogError(exception, "Payment could not be registered.");
                var number = (exception.GetBaseException() as SqlException)?.Number;
                var message = number switch
                {
                    2601 or 2627 => "Оплату з таким номером документа вже зареєстровано.",
                    51030 or 1205 => "Інша фінансова операція ще виконується. Повторіть реєстрацію.",
                    51031 or 51018 => "Оплати реєструє лише активний бухгалтер.",
                    547 => "Рахунок більше не доступний. Оновіть сторінку.",
                    8115 => "Сума виходить за допустимий діапазон. Перевірте дані.",
                    _ => "Не вдалося підтвердити реєстрацію оплати. Перевірте список за номером документа перед повторною спробою."
                };
                ModelState.AddModelError(number is 2601 or 2627 ? nameof(model.Reference) : "", message);
            }
        }
        return View(await FillAsync(model, cancellationToken));
    }

    private async Task<List<SelectListItem>> AccountOptionsAsync(CancellationToken cancellationToken)
    {
        var accounts = await database.Accounts.AsNoTracking().Include(x => x.Owner)
            .OrderBy(x => x.AccountNumber).ToListAsync(cancellationToken);
        return accounts.Select(x => new SelectListItem(
            $"{x.AccountNumber} — {x.Owner.FullName} ({AccountStatuses.DisplayName(x)})", x.AccountId.ToString())).ToList();
    }

    private async Task<PaymentFormViewModel> FillAsync(PaymentFormViewModel model, CancellationToken cancellationToken)
    {
        model.Accounts = await AccountOptionsAsync(cancellationToken);
        return model;
    }
}
