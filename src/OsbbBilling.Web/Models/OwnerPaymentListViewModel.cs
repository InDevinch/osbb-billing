using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class OwnerPaymentListViewModel
{
    public OwnerAccountViewModel Account { get; init; } = new();
    public IReadOnlyList<OwnerPaymentRow> Payments { get; init; } = [];
    [DataType(DataType.Date)]
    public DateOnly? DateFrom { get; init; }
    [DataType(DataType.Date)]
    public DateOnly? DateTo { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}

public class OwnerPaymentRow
{
    public DateOnly PaymentDate { get; init; }
    public string Reference { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal AllocatedAmount { get; init; }
    public decimal CreditAmount { get; init; }
}
