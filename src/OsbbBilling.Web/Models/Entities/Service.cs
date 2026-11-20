namespace OsbbBilling.Web.Models.Entities;

public class Service
{
    public int ServiceId { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string CalculationMethod { get; set; } = "";

    public ICollection<Tariff> Tariffs { get; set; } = new List<Tariff>();
    public ICollection<PremiseService> PremiseServices { get; set; } = new List<PremiseService>();
}
