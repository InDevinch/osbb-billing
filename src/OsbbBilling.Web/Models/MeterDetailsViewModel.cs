using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class MeterDetailsViewModel
{
    public Meter Meter { get; init; } = null!;
    public IReadOnlyList<MeterReadingRowViewModel> Readings { get; init; } = [];
}

public class MeterReadingRowViewModel
{
    public int MeterReadingId { get; init; }
    public DateOnly Period { get; init; }
    public DateOnly ReadingDate { get; init; }
    public decimal ReadingValue { get; init; }
    public decimal? Consumption { get; init; }
    public bool IsUsed { get; init; }
}
