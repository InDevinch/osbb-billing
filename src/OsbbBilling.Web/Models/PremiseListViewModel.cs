using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class PremiseListViewModel
{
    public IReadOnlyList<Premise> Premises { get; init; } = [];
    public IReadOnlyList<SelectListItem> Buildings { get; init; } = [];
    public int? BuildingId { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}
