namespace OsbbBilling.Web.Models;

public class OwnerAccountViewModel
{
    public int AccountId { get; init; }
    public string AccountNumber { get; init; } = "";
    public string Address { get; init; } = "";
    public string PremiseNumber { get; init; } = "";
    public DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
}
