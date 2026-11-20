namespace OsbbBilling.Web.Models.Entities;

public class Payment
{
    public int PaymentId { get; set; }
    public int AccountId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

    public Account Account { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
    public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
}
