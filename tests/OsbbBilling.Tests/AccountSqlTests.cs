using System.Net;
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
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public sealed class SqlAccountFactAttribute : FactAttribute
{
    public SqlAccountFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OSBB_SQLSERVER_TEST_CONNECTION_STRING")))
            Skip = "Set OSBB_SQLSERVER_TEST_CONNECTION_STRING to run isolated SQL write tests.";
    }
}

[Trait("Category", "Integration")]
public class AccountSqlTests : IAsyncLifetime
{
    private readonly string databaseName = "OSBB_Billing_AccountsTest_" + Guid.NewGuid().ToString("N");
    private string? masterConnection;
    private string connection = "";
    private SqlAccountFactory? factory;

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
            factory = new SqlAccountFactory(connection);
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
        if (!Regex.IsMatch(databaseName, "^OSBB_Billing_AccountsTest_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unexpected test database name.");
        await ExecuteAsync($"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """, useMaster: true);
    }

    private async Task ExecuteAsync(string sql, bool useMaster = false)
    {
        await using var sqlConnection = new SqlConnection(useMaster ? masterConnection : connection);
        await sqlConnection.OpenAsync();
        await using var command = sqlConnection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var sqlConnection = new SqlConnection(connection);
        await sqlConnection.OpenAsync();
        await using var command = sqlConnection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private HttpClient Client() => factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
    });

    private static Dictionary<string, string> ChangeFields(string number) => new()
    {
        ["OwnerId"] = "2", ["AccountNumber"] = number, ["ValidFrom"] = "2026-12-01"
    };

    private async Task AssertFinancialHistoryAsync()
    {
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE AccountId=1"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE AccountId=1"));
        Assert.Equal(73.75m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=1"));
        Assert.Equal(3281.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges"));
    }

    [SqlAccountFact]
    public async Task PayerChangePreservesLedgerAndOwnerIsolation()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await SubmitAsync(client, "/Accounts/ChangePayer/1", ChangeFields("QA-NEXT"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new DateTime(2026, 12, 1), await ScalarAsync<DateTime>("SELECT ValidTo FROM dbo.Accounts WHERE AccountId=1"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT OwnerId FROM dbo.Accounts WHERE AccountId=1"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT OwnerId FROM dbo.Accounts WHERE AccountNumber='QA-NEXT'"));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT TotalPaid FROM dbo.v_AccountBalances WHERE AccountNumber='QA-NEXT'"));
        await AssertFinancialHistoryAsync();
        using var oldOwner = Client();
        await LoginAsync(oldOwner, "owner1", "OsbbDemo2026!");
        var oldHtml = await oldOwner.GetStringAsync("/Workspace/Owner");
        Assert.Contains("OSBB-0001", oldHtml);
        Assert.DoesNotContain("QA-NEXT", oldHtml);
        using var newOwner = Client();
        await LoginAsync(newOwner, "owner2", "OsbbDemo2026!");
        var newHtml = await newOwner.GetStringAsync("/Workspace/Owner");
        Assert.Contains("QA-NEXT", newHtml);
        Assert.DoesNotContain("OSBB-0001", newHtml);
    }

    [SqlAccountFact]
    public async Task FailedNewAccountInsertRollsBackPreviousAccountClosure()
    {
        await ExecuteAsync("""
            CREATE TRIGGER dbo.tr_QaRejectAccount ON dbo.Accounts AFTER INSERT AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted WHERE AccountNumber='QA-FAIL')
                    THROW 52011, N'Test insert failure', 1;
            END
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await SubmitAsync(client, "/Accounts/ChangePayer/1", ChangeFields("QA-FAIL"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Операцію скасовано", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(DBNull.Value, await ScalarAsync<object>("SELECT ValidTo FROM dbo.Accounts WHERE AccountId=1"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Accounts"));
        await AssertFinancialHistoryAsync();
        var retry = await SubmitAsync(client, "/Accounts/ChangePayer/1", ChangeFields("QA-RETRY"));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
    }

    [SqlAccountFact]
    public async Task ConcurrentChangesHaveExactlyOneWinner()
    {
        using var first = Client();
        using var second = Client();
        await LoginAsync(first, "accountant", "OsbbDemo2026!");
        await LoginAsync(second, "accountant", "OsbbDemo2026!");
        var firstForm = ChangeFields("QA-FIRST");
        firstForm["__RequestVerificationToken"] = await TokenAsync(first, "/Accounts/ChangePayer/1");
        firstForm["__Invariant"] = "ValidFrom";
        var secondForm = ChangeFields("QA-SECOND");
        secondForm["__RequestVerificationToken"] = await TokenAsync(second, "/Accounts/ChangePayer/1");
        secondForm["__Invariant"] = "ValidFrom";
        var responses = await Task.WhenAll(
            first.PostAsync("/Accounts/ChangePayer/1", new FormUrlEncodedContent(firstForm)),
            second.PostAsync("/Accounts/ChangePayer/1", new FormUrlEncodedContent(secondForm)));
        foreach (var response in responses)
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
                string.Join("\n", (await response.Content.ReadAsStringAsync()).Split('\n').Take(35)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Accounts"));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Accounts WHERE PremiseId=1 AND ValidTo IS NULL"));
        await AssertFinancialHistoryAsync();
    }

    [SqlAccountFact]
    public async Task SqlIntervalsAllowAdjacentAccountsAndRejectChargesCutoff()
    {
        await ExecuteAsync("INSERT dbo.Premises(BuildingId,Number,Kind,Area,ResidentsCount) VALUES(1,N'3','Apartment',40,0)");
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var fields = new Dictionary<string, string>
        {
            ["AccountNumber"] = "QA-PAST", ["OwnerId"] = "1", ["PremiseId"] = "3",
            ["ValidFrom"] = "2026-01-01", ["ValidTo"] = "2026-03-01"
        };
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Accounts/Create", fields)).StatusCode);
        fields["AccountNumber"] = "QA-ADJACENT";
        fields["OwnerId"] = "2";
        fields["ValidFrom"] = "2026-03-01";
        fields["ValidTo"] = "";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Accounts/Create", fields)).StatusCode);
        fields["AccountNumber"] = "QA-OVERLAP";
        fields["ValidFrom"] = "2026-02-01";
        var overlap = await SubmitAsync(client, "/Accounts/Create", fields);
        Assert.Contains("перетинається", WebUtility.HtmlDecode(await overlap.Content.ReadAsStringAsync()));
        var change = ChangeFields("QA-TOO-EARLY");
        change["ValidFrom"] = "2026-11-01";
        var cutoff = await SubmitAsync(client, "/Accounts/ChangePayer/1", change);
        Assert.Contains("уже є нарахування", WebUtility.HtmlDecode(await cutoff.Content.ReadAsStringAsync()));
        Assert.Equal(DBNull.Value, await ScalarAsync<object>("SELECT ValidTo FROM dbo.Accounts WHERE AccountId=1"));
        await AssertFinancialHistoryAsync();
    }

    private sealed class SqlAccountFactory(string sqlConnection) : WebApplicationFactory<Program>
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
