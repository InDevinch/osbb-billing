using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;

namespace OsbbBilling.Web.Services;

public class DatabaseConnectionCheck(IConfiguration configuration, OsbbDbContext database)
{
    public async Task<DatabaseStatusViewModel> CheckAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("OsbbBilling");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new() { Message = "Підключення не налаштоване. Додайте ConnectionStrings:OsbbBilling у User Secrets." };
        }

        try
        {
            var settings = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 5 };
            await using var connection = new SqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            // Запит читає лише метадані та не змінює базу.
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0),
                    (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0),
                    (SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped = 0);
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var tableCount = reader.GetInt32(0);
            var viewCount = reader.GetInt32(1);
            var procedureCount = reader.GetInt32(2);
            await reader.DisposeAsync();

            var entityCounts = new Dictionary<string, int>
            {
                ["Buildings"] = await database.Buildings.CountAsync(cancellationToken),
                ["Owners"] = await database.Owners.CountAsync(cancellationToken),
                ["Premises"] = await database.Premises.CountAsync(cancellationToken),
                ["Accounts"] = await database.Accounts.CountAsync(cancellationToken),
                ["Users"] = await database.Users.CountAsync(cancellationToken),
                ["Services"] = await database.Services.CountAsync(cancellationToken),
                ["Tariffs"] = await database.Tariffs.CountAsync(cancellationToken),
                ["PremiseServices"] = await database.PremiseServices.CountAsync(cancellationToken),
                ["Meters"] = await database.Meters.CountAsync(cancellationToken),
                ["MeterReadings"] = await database.MeterReadings.CountAsync(cancellationToken),
                ["Charges"] = await database.Charges.CountAsync(cancellationToken),
                ["Payments"] = await database.Payments.CountAsync(cancellationToken),
                ["PaymentAllocations"] = await database.PaymentAllocations.CountAsync(cancellationToken)
            };
            return new()
            {
                IsConnected = true,
                Message = "З’єднання з SQL Server встановлено. Читання через EF Core працює.",
                TableCount = tableCount,
                ViewCount = viewCount,
                ProcedureCount = procedureCount,
                EntityCounts = entityCounts
            };
        }
        catch (SqlException)
        {
            return new() { Message = "Не вдалося підключитися до БД. Перевірте SQL Server, назву бази та права доступу." };
        }
        catch (ArgumentException)
        {
            return new() { Message = "Неправильний формат рядка підключення. Перевірте локальні налаштування." };
        }
    }
}
