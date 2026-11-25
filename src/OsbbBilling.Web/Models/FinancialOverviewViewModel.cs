using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class FinancialOverviewViewModel
{
    public IReadOnlyList<SelectListItem> Buildings { get; init; } = [];
    public int? BuildingId { get; init; }
    public int AccountCount { get; init; }
    public int DebtorCount { get; init; }
    public int CreditAccountCount { get; init; }
    public decimal TotalCharged { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal TotalDebt { get; init; }
    public decimal TotalCredit { get; init; }
}
