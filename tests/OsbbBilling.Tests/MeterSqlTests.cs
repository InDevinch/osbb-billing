using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class MeterSqlTests : SqlDatabaseTestBase
{
    private static Dictionary<string, string> Reading(int meterId, string period, string date, string value) => new()
    {
        ["MeterId"] = meterId.ToString(), ["Period"] = period, ["ReadingDate"] = date, ["ReadingValue"] = value
    };

    [SqlServerWriteFact]
    public async Task ReplacementMeterUsesItsOwnInitialReadingAndPreservesOldLedger()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/Meters/Remove/1", new() { ["RemovedOn"] = "2026-12-01" })).StatusCode);
        var installed = await SubmitAsync(client, "/Meters/Create", new()
        {
            ["PremiseServiceId"] = "3", ["SerialNumber"] = "QA-NEW", ["InstalledOn"] = "2026-12-01",
            ["RemovedOn"] = "", ["InitialReading"] = "10.125"
        });
        Assert.Equal("/Meters/Details/3", installed.Headers.Location!.ToString());
        var reading = await SubmitAsync(client, "/MeterReadings/Create?meterId=3", Reading(3, "2026-12-01", "2026-12-31", "10.125"));
        Assert.Equal(HttpStatusCode.Redirect, reading.StatusCode);
        var html = await client.GetStringAsync("/Meters/Details/3");
        Assert.Contains(">0</td>", html);
        Assert.Equal(100m, await ScalarAsync<decimal>("SELECT InitialReading FROM dbo.Meters WHERE MeterId=1"));
        Assert.Equal(10.125m, await ScalarAsync<decimal>("SELECT ReadingValue FROM dbo.MeterReadings WHERE MeterId=3"));
        Assert.Equal(4, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.MeterReadings WHERE MeterId IN (1,2)"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task UnbilledCorrectionsRespectBothNeighboursAndImmutableMonth()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/MeterReadings/Create?meterId=1", Reading(1, "2026-12-01", "2026-12-31", "112.125"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/MeterReadings/Create?meterId=1", Reading(1, "2027-01-01", "2027-01-31", "118"))).StatusCode);
        var invalid = await SubmitAsync(client, "/MeterReadings/Edit/5", Reading(1, "2026-12-01", "2026-12-30", "119"));
        Assert.Contains("перевищувати наступне", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        var corrected = await SubmitAsync(client, "/MeterReadings/Edit/5", Reading(2, "2027-02-01", "2026-12-30", "111.875"));
        Assert.Equal(HttpStatusCode.Redirect, corrected.StatusCode);
        Assert.Equal(1, await ScalarAsync<int>("SELECT MeterId FROM dbo.MeterReadings WHERE MeterReadingId=5"));
        Assert.Equal(new DateTime(2026, 12, 1), await ScalarAsync<DateTime>("SELECT Period FROM dbo.MeterReadings WHERE MeterReadingId=5"));
        Assert.Equal(111.875m, await ScalarAsync<decimal>("SELECT ReadingValue FROM dbo.MeterReadings WHERE MeterReadingId=5"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task ReadingUsedOnlyAsPreviousMonthIsAlsoProtected()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        foreach (var row in new[]
        {
            Reading(1, "2026-12-01", "2026-12-31", "112"),
            Reading(2, "2026-12-01", "2026-12-31", "215"),
            Reading(1, "2027-01-01", "2027-01-31", "118"),
            Reading(2, "2027-01-01", "2027-01-31", "219")
        })
            Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/MeterReadings/Create?meterId=" + row["MeterId"], row)).StatusCode);
        await ExecuteAsync("EXEC dbo.usp_GenerateMonthlyCharges @Period='20270101',@CreatedBy=1");
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE Period='20261201'"));
        var response = await SubmitAsync(client, "/MeterReadings/Edit/5", Reading(1, "2026-12-01", "2026-12-30", "111"));
        Assert.Contains("вже використане", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(112m, await ScalarAsync<decimal>("SELECT ReadingValue FROM dbo.MeterReadings WHERE MeterReadingId=5"));
    }

    [SqlServerWriteFact]
    public async Task ConcurrentReadingsForSameMonthCreateOneRecord()
    {
        using var first = Client();
        using var second = Client();
        await LoginAsync(first, "accountant", "OsbbDemo2026!");
        await LoginAsync(second, "accountant", "OsbbDemo2026!");
        var a = Reading(1, "2026-12-01", "2026-12-31", "112");
        var b = Reading(1, "2026-12-01", "2026-12-31", "113");
        a["__RequestVerificationToken"] = await TokenAsync(first, "/MeterReadings/Create?meterId=1");
        b["__RequestVerificationToken"] = await TokenAsync(second, "/MeterReadings/Create?meterId=1");
        var av = a.ToList();
        var bv = b.ToList();
        foreach (var name in new[] { "Period", "ReadingDate", "ReadingValue" })
        {
            av.Add(new("__Invariant", name));
            bv.Add(new("__Invariant", name));
        }
        var responses = await Task.WhenAll(
            first.PostAsync("/MeterReadings/Create", new FormUrlEncodedContent(av)),
            second.PostAsync("/MeterReadings/Create", new FormUrlEncodedContent(bv)));
        foreach (var response in responses)
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
                string.Join("\n", (await response.Content.ReadAsStringAsync()).Split('\n').Take(30)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.MeterReadings WHERE MeterId=1 AND Period='20261201'"));
        await AssertFinancialHistoryAsync();
    }
}
