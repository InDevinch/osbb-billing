using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class OwnerReadingListViewModel
{
    public OwnerAccountViewModel Account { get; init; } = new();
    public IReadOnlyList<OwnerReadingRow> Readings { get; init; } = [];
    [DataType(DataType.Date)]
    public DateOnly? Period { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}

public class OwnerReadingRow
{
    public DateOnly Period { get; init; }
    public DateOnly ReadingDate { get; init; }
    public string ServiceName { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal ReadingValue { get; init; }
}
