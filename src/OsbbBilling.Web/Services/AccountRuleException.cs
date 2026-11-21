namespace OsbbBilling.Web.Services;

public class AccountRuleException(string message, string field = "") : Exception(message)
{
    public string Field { get; } = field;
}
