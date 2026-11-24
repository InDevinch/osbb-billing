namespace OsbbBilling.Web.Models;

public class OwnerAccountDetailsViewModel
{
    public OwnerAccountViewModel Account { get; init; } = new();
    public AccountBalanceViewModel Balance { get; init; } = new();
}
