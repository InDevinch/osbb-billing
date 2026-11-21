using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class CatalogSqlTests : SqlDatabaseTestBase
{
    private static Dictionary<string, string> VersionFields(decimal price) => new()
    {
        ["BuildingId"] = "1", ["ServiceId"] = "1",
        ["UnitPrice"] = price.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["ValidFrom"] = "2026-12-01", ["ValidTo"] = ""
    };

    [SqlServerWriteFact]
    public async Task NewTariffVersionPreservesPricesAndPostedCharges()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await SubmitAsync(client, "/Tariffs/NewVersion/2", VersionFields(9.1234m));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(8.5m, await ScalarAsync<decimal>("SELECT UnitPrice FROM dbo.Tariffs WHERE TariffId=2"));
        Assert.Equal(new DateTime(2026, 12, 1), await ScalarAsync<DateTime>("SELECT ValidTo FROM dbo.Tariffs WHERE TariffId=2"));
        Assert.Equal(9.1234m, await ScalarAsync<decimal>("SELECT UnitPrice FROM dbo.Tariffs WHERE BuildingId=1 AND ServiceId=1 AND ValidTo IS NULL"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE TariffId=2"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task FailedVersionInsertRollsBackTariffClosure()
    {
        await ExecuteAsync("""
            CREATE TRIGGER dbo.tr_QaTariffFailure ON dbo.Tariffs AFTER INSERT AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted WHERE UnitPrice=888.1234)
                    THROW 52012, N'Test tariff insert failure', 1;
            END
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await SubmitAsync(client, "/Tariffs/NewVersion/2", VersionFields(888.1234m));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Операцію скасовано", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(DBNull.Value, await ScalarAsync<object>("SELECT ValidTo FROM dbo.Tariffs WHERE TariffId=2"));
        Assert.Equal(5, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Tariffs"));
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/Tariffs/NewVersion/2", VersionFields(9m))).StatusCode);
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task ConcurrentVersionsLeaveOneOpenTariff()
    {
        using var first = Client();
        using var second = Client();
        await LoginAsync(first, "accountant", "OsbbDemo2026!");
        await LoginAsync(second, "accountant", "OsbbDemo2026!");
        var firstFields = VersionFields(9.1m);
        firstFields["__RequestVerificationToken"] = await TokenAsync(first, "/Tariffs/NewVersion/2");
        var secondFields = VersionFields(9.2m);
        secondFields["__RequestVerificationToken"] = await TokenAsync(second, "/Tariffs/NewVersion/2");
        var firstValues = firstFields.ToList();
        var secondValues = secondFields.ToList();
        foreach (var field in new[] { "UnitPrice", "ValidFrom" })
        {
            firstValues.Add(new("__Invariant", field));
            secondValues.Add(new("__Invariant", field));
        }
        var responses = await Task.WhenAll(
            first.PostAsync("/Tariffs/NewVersion/2", new FormUrlEncodedContent(firstValues)),
            second.PostAsync("/Tariffs/NewVersion/2", new FormUrlEncodedContent(secondValues)));
        foreach (var response in responses)
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
                string.Join("\n", (await response.Content.ReadAsStringAsync()).Split('\n').Take(30)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Tariffs WHERE BuildingId=1 AND ServiceId=1 AND ValidTo IS NULL"));
        Assert.Equal(6, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Tariffs"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task StoppingAssignmentsProtectsMetersAndHistoricalCharges()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var meter = await SubmitAsync(client, "/PremiseServices/Stop/3", new() { ["ValidTo"] = "2026-12-01" });
        Assert.Contains("роботу лічильника", WebUtility.HtmlDecode(await meter.Content.ReadAsStringAsync()));
        var tooEarly = await SubmitAsync(client, "/PremiseServices/Stop/1", new() { ["ValidTo"] = "2026-11-01" });
        Assert.Contains("виключає наявні", WebUtility.HtmlDecode(await tooEarly.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/PremiseServices/Stop/1", new() { ["ValidTo"] = "2026-12-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/PremiseServices/Create", new()
        {
            ["PremiseId"] = "1", ["ServiceId"] = "1", ["ValidFrom"] = "2026-12-01", ["ValidTo"] = ""
        })).StatusCode);
        Assert.Equal(new DateTime(2026, 12, 1), await ScalarAsync<DateTime>("SELECT ValidTo FROM dbo.PremiseServices WHERE PremiseServiceId=1"));
        Assert.Equal(DBNull.Value, await ScalarAsync<object>("SELECT ValidTo FROM dbo.PremiseServices WHERE PremiseServiceId=3"));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE PremiseServiceId=1"));
        await AssertFinancialHistoryAsync();
    }
}
