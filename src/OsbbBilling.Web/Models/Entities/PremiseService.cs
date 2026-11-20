namespace OsbbBilling.Web.Models.Entities;

public class PremiseService
{
    public int PremiseServiceId { get; set; }
    public int PremiseId { get; set; }
    public int ServiceId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public Premise Premise { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public ICollection<Meter> Meters { get; set; } = new List<Meter>();
    public ICollection<Charge> Charges { get; set; } = new List<Charge>();
}
