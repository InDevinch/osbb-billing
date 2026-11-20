namespace OsbbBilling.Web.Models;

public class DatabaseStatusViewModel
{
    public bool IsConnected { get; init; }
    public string Message { get; init; } = "";
    public int TableCount { get; init; }
    public int ViewCount { get; init; }
    public int ProcedureCount { get; init; }
    public IReadOnlyDictionary<string, int> EntityCounts { get; init; } = new Dictionary<string, int>();
}
