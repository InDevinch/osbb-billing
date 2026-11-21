using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class ServiceListViewModel
{
    public IReadOnlyList<Service> Services { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}
