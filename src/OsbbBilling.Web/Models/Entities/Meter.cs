namespace OsbbBilling.Web.Models.Entities;

public class Meter
{
    public int MeterId { get; set; }
    public int PremiseServiceId { get; set; }
    public string SerialNumber { get; set; } = "";
    public DateOnly InstalledOn { get; set; }
    public DateOnly? RemovedOn { get; set; }
    public decimal InitialReading { get; set; }

    public PremiseService PremiseService { get; set; } = null!;
    public ICollection<MeterReading> MeterReadings { get; set; } = new List<MeterReading>();
}
