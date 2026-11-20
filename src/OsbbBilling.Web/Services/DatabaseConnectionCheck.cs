using Microsoft.Data.SqlClient;
using OsbbBilling.Web.Models;

namespace OsbbBilling.Web.Services;

public class DatabaseConnectionCheck(IConfiguration configuration)
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
            return new()
            {
                IsConnected = true,
                Message = "З’єднання з SQL Server встановлено. Метадані бази доступні.",
                TableCount = reader.GetInt32(0),
                ViewCount = reader.GetInt32(1),
                ProcedureCount = reader.GetInt32(2)
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
