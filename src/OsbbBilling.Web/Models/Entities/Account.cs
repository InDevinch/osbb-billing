namespace OsbbBilling.Web.Models.Entities;

public class Account
{
    public int AccountId { get; set; }
    public string AccountNumber { get; set; } = "";
    public int OwnerId { get; set; }
    public int PremiseId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public Owner Owner { get; set; } = null!;
    public Premise Premise { get; set; } = null!;
    public ICollection<Charge> Charges { get; set; } = new List<Charge>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
