using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class ChargeListViewModel
{
    public IReadOnlyList<Charge> Charges { get; init; } = [];
    public IReadOnlyList<SelectListItem> Accounts { get; init; } = [];
    public IReadOnlyList<SelectListItem> Buildings { get; init; } = [];
    [DataType(DataType.Date)]
    public DateOnly? Period { get; init; }
    public int? AccountId { get; init; }
    public int? BuildingId { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public decimal TotalAmount { get; init; }
}
