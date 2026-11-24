using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class OwnerChargeListViewModel
{
    public OwnerAccountViewModel Account { get; init; } = new();
    public IReadOnlyList<OwnerChargeRow> Charges { get; init; } = [];
    [DataType(DataType.Date)]
    public DateOnly? Period { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}

public class OwnerChargeRow
{
    public DateOnly Period { get; init; }
    public string ServiceName { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Amount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal DebtAmount { get; init; }
}
