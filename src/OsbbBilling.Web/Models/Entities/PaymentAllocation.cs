namespace OsbbBilling.Web.Models.Entities;

public class PaymentAllocation
{
    public int PaymentId { get; set; }
    public int ChargeId { get; set; }
    public decimal Amount { get; set; }

    public Payment Payment { get; set; } = null!;
    public Charge Charge { get; set; } = null!;
}
