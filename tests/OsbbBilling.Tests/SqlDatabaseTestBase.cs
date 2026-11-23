using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OsbbBilling.Web.Data;

namespace OsbbBilling.Tests;

public sealed class SqlServerWriteFactAttribute : FactAttribute
{
    public SqlServerWriteFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OSBB_SQLSERVER_TEST_CONNECTION_STRING")))
            Skip = "Set OSBB_SQLSERVER_TEST_CONNECTION_STRING to run isolated SQL write tests.";
    }
}

public abstract class SqlDatabaseTestBase : IAsyncLifetime
{
    private readonly string databaseName = "OSBB_Billing_IntegrationTest_" + Guid.NewGuid().ToString("N");
    private string? masterConnection;
    private string connection = "";
    private SqlTestApplicationFactory? factory;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("OSBB_SQLSERVER_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var settings = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        masterConnection = settings.ConnectionString;
        settings.InitialCatalog = databaseName;
        connection = settings.ConnectionString;
        try
        {
            foreach (var script in new[] { "01_CreateDatabase.sql", "02_DemoData.sql" })
            {
                var sql = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", script)))
                    .Replace("OSBB_Billing", databaseName);
                await ExecuteAsync(sql, useMaster: script == "01_CreateDatabase.sql");
            }
            factory = new SqlTestApplicationFactory(connection);
        }
        catch
        {
            await DropDatabaseAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await DropDatabaseAsync();
    }

    private async Task DropDatabaseAsync()
    {
        if (masterConnection is null) return;
        if (!Regex.IsMatch(databaseName, "^OSBB_Billing_IntegrationTest_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unexpected test database name.");
        await ExecuteAsync($"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """, useMaster: true);
    }

    protected async Task ExecuteAsync(string sql, bool useMaster = false)
    {
        await using var sqlConnection = new SqlConnection(useMaster ? masterConnection : connection);
        await sqlConnection.OpenAsync();
        await using var command = sqlConnection.CreateCommand();
        // Створення та видалення тестової БД може тривати довше на завантаженому сервері.
        command.CommandTimeout = 180;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    protected async Task<T> ScalarAsync<T>(string sql)
    {
        await using var sqlConnection = new SqlConnection(connection);
        await sqlConnection.OpenAsync();
        await using var command = sqlConnection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    protected HttpClient Client() => factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
    });

    protected async Task AssertFinancialHistoryAsync()
    {
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE AccountId=1"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE AccountId=1"));
        Assert.Equal(73.75m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=1"));
        Assert.Equal(3281.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges"));
    }

    private sealed class SqlTestApplicationFactory(string sqlConnection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<OsbbDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<OsbbDbContext>>();
                services.AddDbContext<OsbbDbContext>(options => options.UseSqlServer(sqlConnection));
            });
        }
    }
}
