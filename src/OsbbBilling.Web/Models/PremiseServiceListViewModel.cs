using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class PremiseServiceListViewModel
{
    public IReadOnlyList<PremiseService> Assignments { get; init; } = [];
    public IReadOnlyList<SelectListItem> Premises { get; init; } = [];
    public IReadOnlyList<SelectListItem> Services { get; init; } = [];
    public int? PremiseId { get; init; }
    public int? ServiceId { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}
