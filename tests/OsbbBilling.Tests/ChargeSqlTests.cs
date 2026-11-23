using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class ChargeSqlTests : SqlDatabaseTestBase
{
    private Task PrepareDecemberAsync() => ExecuteAsync("""
        INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue)
        VALUES(1,'20261201','20261231',112),(2,'20261201','20261231',215);
        """);

    private static Task<HttpResponseMessage> GenerateAsync(HttpClient client) => SubmitAsync(client,
        "/Charges/Generate", new() { ["Period"] = "2026-12-01", ["CreatedBy"] = "2" });

    [SqlServerWriteFact]
    public async Task GenerationUsesAllFourMethodsAuthenticatedAuthorAndExistingCredit()
    {
        await PrepareDecemberAsync();
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect, (await GenerateAsync(client)).StatusCode);
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201' AND CreatedBy=1"));
        Assert.Equal(1491.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE Period='20261201'"));
        Assert.Equal(3281.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE Period<'20261201'"));
        Assert.Equal(636.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE AccountId=1 AND Period='20261201'"));
        Assert.Equal(4, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Charges WHERE AccountId=1 AND Period='20261201' AND
            ((PremiseServiceId=1 AND Quantity=52.5 AND UnitPrice=8.5 AND Amount=446.25)
             OR (PremiseServiceId=2 AND Quantity=2 AND UnitPrice=40 AND Amount=80)
             OR (PremiseServiceId=3 AND Quantity=2 AND UnitPrice=30 AND Amount=60)
             OR (PremiseServiceId=4 AND Quantity=1 AND UnitPrice=50 AND Amount=50));
            """));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=1"));
        Assert.Equal(562.50m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=1"));
        Assert.Equal(2310m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Charges?period=2026-12-01&accountId=1&buildingId=1"));
        Assert.Contains("Знайдено: 4", html);
        Assert.Matches("636[.,]25 грн", html);
        var id = await ScalarAsync<int>("SELECT MIN(ChargeId) FROM dbo.Charges WHERE Period='20261201'");
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/Charges/Details/{id}"));
        Assert.Contains("accountant", details);
        Assert.Contains("12.2026", details);
        Assert.Equal(HttpStatusCode.Redirect, (await GenerateAsync(client)).StatusCode);
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
        Assert.Equal(1491.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE Period='20261201'"));
        Assert.Equal(562.50m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=1"));
    }

    [SqlServerWriteFact]
    public async Task FractionalConsumptionAndTariffAreRoundedOnlyInFinalAmount()
    {
        await PrepareDecemberAsync();
        await ExecuteAsync("""
            UPDATE dbo.MeterReadings SET ReadingValue=112.125 WHERE MeterId=1 AND Period='20261201';
            UPDATE dbo.Tariffs SET ValidTo='20261201' WHERE TariffId=4;
            INSERT dbo.Tariffs(BuildingId,ServiceId,UnitPrice,ValidFrom) VALUES(1,3,30.1234,'20261201');
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect, (await GenerateAsync(client)).StatusCode);
        Assert.Equal(2.125m, await ScalarAsync<decimal>("SELECT Quantity FROM dbo.Charges WHERE PremiseServiceId=3 AND Period='20261201'"));
        Assert.Equal(30.1234m, await ScalarAsync<decimal>("SELECT UnitPrice FROM dbo.Charges WHERE PremiseServiceId=3 AND Period='20261201'"));
        Assert.Equal(64.01m, await ScalarAsync<decimal>("SELECT Amount FROM dbo.Charges WHERE PremiseServiceId=3 AND Period='20261201'"));
        Assert.Equal(3281.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE Period<'20261201'"));
    }

    [SqlServerWriteFact]
    public async Task MissingReadingCancelsWholeMonthAndPreservesCredit()
    {
        await ExecuteAsync("INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue) VALUES(1,'20261201','20261231',112)");
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await GenerateAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("попереднього показання", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task MissingTariffOrAccountCancelsWholeMonth()
    {
        await PrepareDecemberAsync();
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        await ExecuteAsync("UPDATE dbo.Tariffs SET ValidTo='20261201' WHERE TariffId=2");
        var response = await GenerateAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("немає рахунку або тарифу", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        await AssertFinancialHistoryAsync();
        await ExecuteAsync("UPDATE dbo.Tariffs SET ValidTo=NULL WHERE TariffId=2; UPDATE dbo.Accounts SET ValidTo='20261201' WHERE AccountId=1;");
        response = await GenerateAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("немає рахунку або тарифу", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task AllocationFailureRollsBackChargesAndCreditThenAllowsRetry()
    {
        await PrepareDecemberAsync();
        await ExecuteAsync("""
            CREATE TRIGGER dbo.tr_QaAllocationFailure ON dbo.PaymentAllocations AFTER INSERT AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.Charges c ON c.ChargeId=i.ChargeId WHERE c.Period='20261201')
                    THROW 52013,N'Test allocation failure',1;
            END
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await GenerateAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Операцію скасовано", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
        await AssertFinancialHistoryAsync();
        await ExecuteAsync("DROP TRIGGER dbo.tr_QaAllocationFailure");
        Assert.Equal(HttpStatusCode.Redirect, (await GenerateAsync(client)).StatusCode);
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
    }

    [SqlServerWriteFact]
    public async Task ConcurrentGenerationDoesNotDuplicateChargesOrCreditAllocations()
    {
        await PrepareDecemberAsync();
        using var first = Client();
        using var second = Client();
        await LoginAsync(first, "accountant", "OsbbDemo2026!");
        await LoginAsync(second, "accountant", "OsbbDemo2026!");
        var responses = await Task.WhenAll(GenerateAsync(first), GenerateAsync(second));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode));
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
        Assert.Equal(1491.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE Period='20261201'"));
        Assert.Equal(73.75m, await ScalarAsync<decimal>("SELECT SUM(a.Amount) FROM dbo.PaymentAllocations a JOIN dbo.Charges c ON c.ChargeId=a.ChargeId WHERE c.Period='20261201'"));
    }

    [SqlServerWriteFact]
    public async Task HistorySupportsFilteringPaginationAndChairReadOnlyAccess()
    {
        await PrepareDecemberAsync();
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        await GenerateAsync(client);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Charges?page=2"));
        Assert.Contains("Знайдено: 24", html);
        Assert.Contains("Сторінка 2 з 2", html);
        Assert.Contains("Знайдено: 8", WebUtility.HtmlDecode(await client.GetStringAsync("/Charges?period=2026-11-01")));
        Assert.Contains("Знайдено: 12", WebUtility.HtmlDecode(await client.GetStringAsync("/Charges?accountId=2")));
        Assert.Contains("Знайдено: 2", WebUtility.HtmlDecode(await client.GetStringAsync("/Charges?period=2026-12-01&search=Холодна")));
        Assert.Contains("Знайдено: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Charges?period=2026-12-02")));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Charges?accountId=999")).StatusCode);
        using var chair = Client();
        await LoginAsync(chair, "chair", "OsbbDemo2026!");
        html = await chair.GetStringAsync("/Charges");
        Assert.DoesNotContain("href=\"/Charges/Generate\"", html);
        Assert.Equal(HttpStatusCode.OK, (await chair.GetAsync("/Charges/Details/1")).StatusCode);
    }
}
