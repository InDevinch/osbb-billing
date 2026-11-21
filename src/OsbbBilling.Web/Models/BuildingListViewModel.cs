namespace OsbbBilling.Web.Models;

public class BuildingListViewModel
{
    public IReadOnlyList<BuildingListItemViewModel> Buildings { get; init; } = [];
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
}

public class BuildingListItemViewModel
{
    public int BuildingId { get; init; }
    public string Address { get; init; } = "";
    public int PremiseCount { get; init; }
}
