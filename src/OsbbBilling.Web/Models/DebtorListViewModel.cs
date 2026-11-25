using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class DebtorListViewModel
{
    public IReadOnlyList<AccountBalanceViewModel> Debtors { get; init; } = [];
    public IReadOnlyList<SelectListItem> Buildings { get; init; } = [];
    public int? BuildingId { get; init; }
    public string? Search { get; init; }
    public decimal? MinimumDebt { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public decimal TotalDebt { get; init; }
}
