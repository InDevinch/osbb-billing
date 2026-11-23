using Microsoft.Data.SqlClient;

namespace OsbbBilling.Web.Services;

public static class SqlReadRetry
{
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        // Повторюємо лише читання, яке SQL Server скасував через взаємне блокування.
        for (var attempt = 0; ; attempt++)
        {
            try { return await read(); }
            catch (Exception exception) when (attempt < 2 && exception.GetBaseException() is SqlException { Number: 1205 })
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
