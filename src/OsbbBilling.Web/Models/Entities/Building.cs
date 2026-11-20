namespace OsbbBilling.Web.Models.Entities;

public class Building
{
    public int BuildingId { get; set; }
    public string Address { get; set; } = "";

    public ICollection<Premise> Premises { get; set; } = new List<Premise>();
    public ICollection<Tariff> Tariffs { get; set; } = new List<Tariff>();
}
