namespace OsbbBilling.Web.Models;

public class AccountBalanceViewModel
{
    public int AccountId { get; init; }
    public string AccountNumber { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public int BuildingId { get; init; }
    public string Address { get; init; } = "";
    public string PremiseNumber { get; init; } = "";
    public decimal TotalCharged { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal DebtAmount { get; init; }
    public decimal CreditAmount { get; init; }
    public decimal NetBalance { get; init; }
}
