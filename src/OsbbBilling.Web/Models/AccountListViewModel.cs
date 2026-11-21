using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class AccountListViewModel
{
    public IReadOnlyList<Account> Accounts { get; init; } = [];
    public IReadOnlyList<SelectListItem> Owners { get; init; } = [];
    public IReadOnlyList<SelectListItem> Premises { get; init; } = [];
    public int? OwnerId { get; init; }
    public int? PremiseId { get; init; }
    public string? Search { get; init; }
    public string Status { get; init; } = "all";
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}
