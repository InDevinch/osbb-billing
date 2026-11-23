using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class AccountDetailsViewModel
{
    public Account Account { get; init; } = null!;
    public IReadOnlyList<Account> History { get; init; } = [];
    public int ChargeCount { get; init; }
    public int PaymentCount { get; init; }
    public AccountBalanceViewModel Balance { get; init; } = new();
}
