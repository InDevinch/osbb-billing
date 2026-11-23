using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class AccountsController(OsbbDbContext database, AccountService accountService, AccountBalanceService balances,
    ILogger<AccountsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? ownerId, int? premiseId, string? search,
        string status = "all", int page = 1, CancellationToken cancellationToken = default)
    {
        if (ownerId.HasValue && !await database.Owners.AnyAsync(x => x.OwnerId == ownerId, cancellationToken))
            return NotFound();
        if (premiseId.HasValue && !await database.Premises.AnyAsync(x => x.PremiseId == premiseId, cancellationToken))
            return NotFound();
        var query = database.Accounts.AsNoTracking();
        if (ownerId.HasValue) query = query.Where(x => x.OwnerId == ownerId);
        if (premiseId.HasValue) query = query.Where(x => x.PremiseId == premiseId);
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.AccountNumber.Contains(search) || x.Owner.FullName.Contains(search)
                || x.Premise.Building.Address.Contains(search) || x.Premise.Number.Contains(search));
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (status == "active")
            query = query.Where(x => x.ValidFrom <= today && (!x.ValidTo.HasValue || x.ValidTo > today));
        else if (status == "planned")
            query = query.Where(x => x.ValidFrom > today);
        else if (status == "closed")
            query = query.Where(x => x.ValidTo.HasValue && x.ValidTo <= today);
        else status = "all";
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        var accounts = await query.Include(x => x.Owner).Include(x => x.Premise).ThenInclude(x => x.Building)
            .OrderBy(x => x.AccountNumber).ThenBy(x => x.AccountId)
            .Skip((page - 1) * 20).Take(20).ToListAsync(cancellationToken);
        return View(new AccountListViewModel
        {
            Accounts = accounts, Owners = await OwnerOptionsAsync(cancellationToken),
            Premises = await PremiseOptionsAsync(cancellationToken), OwnerId = ownerId, PremiseId = premiseId,
            Search = search, Status = status, Page = page, TotalPages = totalPages, TotalCount = count
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        var balance = await balances.FindAsync(id, cancellationToken);
        if (balance is null) return NotFound();
        var history = await database.Accounts.AsNoTracking().Include(x => x.Owner)
            .Where(x => x.PremiseId == account.PremiseId)
            .OrderBy(x => x.ValidFrom).ThenBy(x => x.AccountId).ToListAsync(cancellationToken);
        return View(new AccountDetailsViewModel
        {
            Account = account, History = history, Balance = balance,
            ChargeCount = await database.Charges.CountAsync(x => x.AccountId == id, cancellationToken),
            PaymentCount = await database.Payments.CountAsync(x => x.AccountId == id, cancellationToken)
        });
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(int? ownerId, int? premiseId, CancellationToken cancellationToken)
    {
        if (ownerId.HasValue && !await database.Owners.AnyAsync(x => x.OwnerId == ownerId, cancellationToken))
            return NotFound();
        if (premiseId.HasValue && !await database.Premises.AnyAsync(x => x.PremiseId == premiseId, cancellationToken))
            return NotFound();
        var today = DateTime.Today;
        return View(await FillOpenFormAsync(new OpenAccountViewModel
        {
            OwnerId = ownerId ?? 0, PremiseId = premiseId ?? 0,
            ValidFrom = new DateOnly(today.Year, today.Month, 1)
        }, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(OpenAccountViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var account = await accountService.OpenAsync(new Account
                {
                    AccountNumber = model.AccountNumber.Trim(), OwnerId = model.OwnerId, PremiseId = model.PremiseId,
                    ValidFrom = model.ValidFrom!.Value, ValidTo = model.ValidTo
                }, cancellationToken);
                TempData["Success"] = "Особовий рахунок відкрито.";
                return RedirectToAction(nameof(Details), new { id = account.AccountId });
            }
            catch (AccountRuleException exception)
            {
                ModelState.AddModelError(exception.Field, exception.Message);
            }
            catch (DbUpdateException exception)
            {
                AddSaveError(exception);
            }
            catch (SqlException exception)
            {
                AddSaveError(exception);
            }
            catch (InvalidOperationException exception) when (exception.GetBaseException() is SqlException { Number: 1205 })
            {
                AddSaveError(exception);
            }
        }
        return View(await FillOpenFormAsync(model, cancellationToken));
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> ChangePayer([FromRoute] int id, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        var nextMonth = DateTime.Today.AddMonths(1);
        return View(await FillChangeFormAsync(new ChangePayerViewModel
        {
            CurrentAccount = account, ValidFrom = new DateOnly(nextMonth.Year, nextMonth.Month, 1)
        }, account, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> ChangePayer([FromRoute] int id, ChangePayerViewModel model,
        CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                var next = await accountService.ChangePayerAsync(id, model.OwnerId,
                    model.AccountNumber.Trim(), model.ValidFrom!.Value, cancellationToken);
                if (next is null) return NotFound();
                TempData["Success"] = "Платника змінено. Попередній рахунок та його фінансова історія збережені.";
                return RedirectToAction(nameof(Details), new { id = next.AccountId });
            }
            catch (AccountRuleException exception)
            {
                ModelState.AddModelError(exception.Field, exception.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError("", "Рахунок уже змінено іншим запитом. Оновіть сторінку.");
            }
            catch (DbUpdateException exception)
            {
                AddSaveError(exception);
            }
            catch (SqlException exception)
            {
                AddSaveError(exception);
            }
            catch (InvalidOperationException exception) when (exception.GetBaseException() is SqlException { Number: 1205 })
            {
                AddSaveError(exception);
            }
        }
        return View(await FillChangeFormAsync(model, account, cancellationToken));
    }

    private Task<Account?> FindAccountAsync(int id, CancellationToken cancellationToken) =>
        SqlReadRetry.ExecuteAsync(() => database.Accounts.AsNoTracking().Include(x => x.Owner).Include(x => x.Premise).ThenInclude(x => x.Building)
            .SingleOrDefaultAsync(x => x.AccountId == id, cancellationToken), cancellationToken);

    private async Task<List<SelectListItem>> OwnerOptionsAsync(CancellationToken cancellationToken) =>
        await database.Owners.AsNoTracking().OrderBy(x => x.FullName).ThenBy(x => x.OwnerId)
            .Select(x => new SelectListItem(x.FullName, x.OwnerId.ToString())).ToListAsync(cancellationToken);

    private async Task<List<SelectListItem>> PremiseOptionsAsync(CancellationToken cancellationToken) =>
        await database.Premises.AsNoTracking().OrderBy(x => x.Building.Address).ThenBy(x => x.Number)
            .Select(x => new SelectListItem(x.Building.Address + " / " + x.Number, x.PremiseId.ToString()))
            .ToListAsync(cancellationToken);

    private async Task<OpenAccountViewModel> FillOpenFormAsync(OpenAccountViewModel model, CancellationToken cancellationToken)
    {
        model.Owners = await OwnerOptionsAsync(cancellationToken);
        model.Premises = await PremiseOptionsAsync(cancellationToken);
        return model;
    }

    private async Task<ChangePayerViewModel> FillChangeFormAsync(ChangePayerViewModel model, Account account,
        CancellationToken cancellationToken)
    {
        model.CurrentAccount = account;
        model.Owners = await OwnerOptionsAsync(cancellationToken);
        return model;
    }

    private void AddSaveError(Exception exception)
    {
        var sql = exception.GetBaseException() as SqlException;
        var message = sql?.Number switch
        {
            2601 or 2627 => "Рахунок із таким номером уже є.",
            51001 => "Період перетинається з іншим рахунком приміщення.",
            51003 => "Дата зміни виключає наявні нарахування.",
            547 => "Платника або приміщення змінено. Перевірте обрані дані.",
            1205 => "Інший запит одночасно змінював дані. Повторіть операцію.",
            _ => "Не вдалося зберегти рахунок. Операцію скасовано. Спробуйте ще раз."
        };
        logger.LogError(exception, "Account operation could not be saved.");
        ModelState.AddModelError("", message);
    }
}
