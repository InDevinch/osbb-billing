using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public static class AccountStatuses
{
    public static string DisplayName(Account account)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (account.ValidFrom > today) return "Запланований";
        if (account.ValidTo.HasValue && account.ValidTo <= today) return "Завершений";
        return "Діє";
    }
}
