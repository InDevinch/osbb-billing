using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class TariffListViewModel
{
    public IReadOnlyList<Tariff> Tariffs { get; init; } = [];
    public IReadOnlyList<SelectListItem> Buildings { get; init; } = [];
    public IReadOnlyList<SelectListItem> Services { get; init; } = [];
    public int? BuildingId { get; init; }
    public int? ServiceId { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}
