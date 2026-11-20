namespace OsbbBilling.Web.Models.Entities;

public class Premise
{
    public int PremiseId { get; set; }
    public int BuildingId { get; set; }
    public string Number { get; set; } = "";
    public string Kind { get; set; } = "";
    public decimal Area { get; set; }
    public short ResidentsCount { get; set; }

    public Building Building { get; set; } = null!;
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<PremiseService> PremiseServices { get; set; } = new List<PremiseService>();
}
