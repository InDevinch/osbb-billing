namespace OsbbBilling.Web.Models.Entities;

public class Charge
{
    public int ChargeId { get; set; }
    public int AccountId { get; set; }
    public int PremiseServiceId { get; set; }
    public int TariffId { get; set; }
    public DateOnly Period { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; private set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    public Account Account { get; set; } = null!;
    public PremiseService PremiseService { get; set; } = null!;
    public Tariff Tariff { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
    public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
}
