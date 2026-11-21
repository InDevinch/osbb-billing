namespace OsbbBilling.Web.Models;

public static class CalculationMethods
{
    public static string DisplayName(string method) => method switch
    {
        "Area" => "За площею",
        "Residents" => "За кількістю мешканців",
        "Meter" => "За лічильником",
        "Fixed" => "Фіксований",
        _ => method
    };
}
