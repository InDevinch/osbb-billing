using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;

namespace OsbbBilling.Web.Services;

public class ChargeGenerationService(OsbbDbContext database)
{
    public async Task GenerateAsync(DateOnly period, int createdBy, CancellationToken cancellationToken)
    {
        if (period.Day != 1)
            throw new CatalogRuleException("Оберіть перше число місяця.", nameof(period));
        var periodParameter = new SqlParameter("@Period", SqlDbType.Date) { Value = period.ToDateTime(TimeOnly.MinValue) };
        var authorParameter = new SqlParameter("@CreatedBy", SqlDbType.Int) { Value = createdBy };
        database.Database.SetCommandTimeout(60);
        // Процедура сама керує транзакцією та блокуванням фінансових операцій.
        await database.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_GenerateMonthlyCharges @Period=@Period, @CreatedBy=@CreatedBy",
            new object[] { periodParameter, authorParameter }, cancellationToken);
    }
}
