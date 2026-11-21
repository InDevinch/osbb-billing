namespace OsbbBilling.Web.Models;

public static class PremiseKinds
{
    public const string Apartment = "Apartment";
    public const string Nonres = "Nonres";

    public static string DisplayName(string kind) => kind switch
    {
        Apartment => "Квартира",
        Nonres => "Нежитлове приміщення",
        _ => kind
    };
}
