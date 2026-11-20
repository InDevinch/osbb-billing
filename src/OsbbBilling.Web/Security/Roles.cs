namespace OsbbBilling.Web.Security;

public static class Roles
{
    public const string Accountant = "Accountant";
    public const string Chairperson = "Chairperson";
    public const string Owner = "Owner";

    public static string DisplayName(string? role) => role switch
    {
        Accountant => "Бухгалтер",
        Chairperson => "Голова ОСББ",
        Owner => "Власник",
        _ => ""
    };
}
