using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Owner)]
public class OwnerAccountsController(OsbbDbContext database, AccountBalanceService balances) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        var balance = await balances.FindAsync(account.AccountId, cancellationToken);
        if (balance is null) return NotFound();
        return View(new OwnerAccountDetailsViewModel { Account = ToViewModel(account), Balance = balance });
    }

    [HttpGet]
    public async Task<IActionResult> Charges([FromRoute] int id, DateOnly? period, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        ValidatePeriod(period);
        var query = database.Charges.AsNoTracking().Where(x => x.AccountId == account.AccountId);
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (period.HasValue) query = query.Where(x => x.Period == period);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new OwnerChargeListViewModel
        {
            Account = ToViewModel(account), Period = period, Page = page, TotalPages = totalPages, TotalCount = count,
            Charges = await query.OrderByDescending(x => x.Period).ThenBy(x => x.ChargeId)
                .Skip((page - 1) * 20).Take(20).Select(x => new OwnerChargeRow
                {
                    Period = x.Period, ServiceName = x.PremiseService.Service.Name, Unit = x.PremiseService.Service.Unit,
                    Quantity = x.Quantity, UnitPrice = x.UnitPrice, Amount = x.Amount,
                    PaidAmount = x.PaymentAllocations.Sum(a => (decimal?)a.Amount) ?? 0,
                    DebtAmount = x.Amount - (x.PaymentAllocations.Sum(a => (decimal?)a.Amount) ?? 0)
                }).ToListAsync(cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Payments([FromRoute] int id, DateOnly? dateFrom, DateOnly? dateTo, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
            ModelState.AddModelError(nameof(OwnerPaymentListViewModel.DateTo), "Дата завершення має бути не раніше початку.");
        var query = database.Payments.AsNoTracking().Where(x => x.AccountId == account.AccountId);
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (dateFrom.HasValue) query = query.Where(x => x.PaymentDate >= dateFrom);
        if (dateTo.HasValue) query = query.Where(x => x.PaymentDate <= dateTo);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new OwnerPaymentListViewModel
        {
            Account = ToViewModel(account), DateFrom = dateFrom, DateTo = dateTo,
            Page = page, TotalPages = totalPages, TotalCount = count,
            Payments = await query.OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.PaymentId)
                .Skip((page - 1) * 20).Take(20).Select(x => new OwnerPaymentRow
                {
                    PaymentDate = x.PaymentDate, Reference = x.Reference, Amount = x.Amount,
                    AllocatedAmount = x.PaymentAllocations.Sum(a => (decimal?)a.Amount) ?? 0,
                    CreditAmount = x.Amount - (x.PaymentAllocations.Sum(a => (decimal?)a.Amount) ?? 0)
                }).ToListAsync(cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Readings([FromRoute] int id, DateOnly? period, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(id, cancellationToken);
        if (account is null) return NotFound();
        ValidatePeriod(period);
        // Показання належать приміщенню, тому обмежуємо їх місяцями дії саме цього рахунку.
        var query = database.MeterReadings.AsNoTracking()
            .Where(x => x.Meter.PremiseService.PremiseId == account.PremiseId && x.Period >= account.ValidFrom);
        if (account.ValidTo.HasValue) query = query.Where(x => x.Period < account.ValidTo.Value);
        if (!ModelState.IsValid) query = query.Where(x => false);
        if (period.HasValue) query = query.Where(x => x.Period == period);
        var count = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / 20.0));
        page = Math.Clamp(page, 1, totalPages);
        return View(new OwnerReadingListViewModel
        {
            Account = ToViewModel(account), Period = period, Page = page, TotalPages = totalPages, TotalCount = count,
            Readings = await query.OrderByDescending(x => x.Period).ThenBy(x => x.MeterReadingId)
                .Skip((page - 1) * 20).Take(20).Select(x => new OwnerReadingRow
                {
                    Period = x.Period, ReadingDate = x.ReadingDate, ReadingValue = x.ReadingValue,
                    SerialNumber = x.Meter.SerialNumber, ServiceName = x.Meter.PremiseService.Service.Name,
                    Unit = x.Meter.PremiseService.Service.Unit
                }).ToListAsync(cancellationToken)
        });
    }

    private Task<Account?> FindAccountAsync(int id, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("OwnerId"), out var ownerId)) return Task.FromResult<Account?>(null);
        // OwnerId береться лише з перевіреної сесії. Чужий і відсутній рахунки повертають однаковий 404.
        return SqlReadRetry.ExecuteAsync(() => database.Accounts.AsNoTracking()
            .Include(x => x.Premise).ThenInclude(x => x.Building)
            .SingleOrDefaultAsync(x => x.AccountId == id && x.OwnerId == ownerId, cancellationToken), cancellationToken);
    }

    private void ValidatePeriod(DateOnly? period)
    {
        if (period.HasValue && period.Value.Day != 1)
            ModelState.AddModelError("Period", "Оберіть перше число місяця.");
    }

    private static OwnerAccountViewModel ToViewModel(Account account) => new()
    {
        AccountId = account.AccountId, AccountNumber = account.AccountNumber,
        Address = account.Premise.Building.Address, PremiseNumber = account.Premise.Number,
        ValidFrom = account.ValidFrom, ValidTo = account.ValidTo
    };
}
