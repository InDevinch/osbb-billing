namespace OsbbBilling.Web.Models.Entities;

public class Tariff
{
    public int TariffId { get; set; }
    public int BuildingId { get; set; }
    public int ServiceId { get; set; }
    public decimal UnitPrice { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public Building Building { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public ICollection<Charge> Charges { get; set; } = new List<Charge>();
}
