namespace OsbbBilling.Web.Services;

public class CatalogRuleException(string message, string field = "") : Exception(message)
{
    public string Field { get; } = field;
}
