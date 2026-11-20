namespace OsbbBilling.Web.Models.Entities;

public class MeterReading
{
    public int MeterReadingId { get; set; }
    public int MeterId { get; set; }
    public DateOnly Period { get; set; }
    public DateOnly ReadingDate { get; set; }
    public decimal ReadingValue { get; set; }

    public Meter Meter { get; set; } = null!;
}
