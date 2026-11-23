using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class MeterListViewModel
{
    public IReadOnlyList<Meter> Meters { get; init; } = [];
    public IReadOnlyList<SelectListItem> Assignments { get; init; } = [];
    public int? PremiseServiceId { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}
