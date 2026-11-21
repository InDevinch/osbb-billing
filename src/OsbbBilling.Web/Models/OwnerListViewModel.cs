namespace OsbbBilling.Web.Models;

public class OwnerListViewModel
{
    public IReadOnlyList<OwnerListItemViewModel> Owners { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}

public class OwnerListItemViewModel
{
    public int OwnerId { get; init; }
    public string FullName { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public int AccountCount { get; init; }
}
