using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class OwnerAccountSqlTests : SqlDatabaseTestBase
{
    [SqlServerWriteFact]
    public async Task OwnerSeesOwnFinancialAmountsReadingsAndPaymentsOnly()
    {
        using var client = Client();
        await LoginAsync(client, "owner1", "OsbbDemo2026!");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Details/1?ownerId=2&accountId=2"));
        Assert.Contains("OSBB-0001", html);
        Assert.DoesNotContain("OSBB-0002", html);
        Assert.Matches("1426[.,]25 грн", html);
        Assert.Matches("1500[.,]00 грн", html);
        Assert.Matches("73[.,]75 грн", html);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Charges/1?period=2026-11-01"));
        Assert.Contains("Знайдено нарахувань: 4", html);
        Assert.Matches(">446[.,]25</td>", html);
        Assert.Matches(">52[.,]5 м²</td>", html);
        Assert.DoesNotMatch(">595[.,]00</td>", html);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Payments/1"));
        Assert.Contains("Знайдено оплат: 2", html);
        Assert.Contains("DEMO-PAY-001", html);
        Assert.Contains("DEMO-PAY-003", html);
        Assert.DoesNotContain("DEMO-PAY-002", html);
        Assert.Matches(">926[.,]25</td>", html);
        Assert.Matches(">73[.,]75</td>", html);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Payments/1?dateFrom=2026-11-30&dateTo=2026-11-30"));
        Assert.Contains("Знайдено оплат: 1", html);
        Assert.DoesNotContain("DEMO-PAY-001", html);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Readings/1?period=2026-11-01"));
        Assert.Contains("Знайдено показань: 1", html);
        Assert.Contains("DEMO-WATER-001", html);
        Assert.DoesNotContain("DEMO-WATER-002", html);
        Assert.Contains(">110 м³</td>", html);
        foreach (var action in new[] { "Details", "Charges", "Payments", "Readings" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/OwnerAccounts/{action}/2")).StatusCode);
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task PayerChangeSeparatesMeterHistoryAndKeepsLatePaymentsOnOldAccount()
    {
        await ExecuteAsync("""
            UPDATE dbo.Accounts SET ValidTo='20261201' WHERE AccountId=1;
            INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES(N'QA-NEXT-OWNER',2,1,'20261201');
            INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue)
            VALUES(1,'20261201','20261231',112),(2,'20261201','20261231',215);
            EXEC dbo.usp_GenerateMonthlyCharges '20261201',1;
            EXEC dbo.usp_RegisterPayment 1,'20270115',10,N'QA-OLD-LATE-PAYMENT',1;
            """);
        using var oldOwner = Client();
        using var nextOwner = Client();
        await LoginAsync(oldOwner, "owner1", "OsbbDemo2026!");
        await LoginAsync(nextOwner, "owner2", "OsbbDemo2026!");
        var oldList = await oldOwner.GetStringAsync("/Workspace/Owner");
        Assert.Contains("OSBB-0001", oldList);
        Assert.DoesNotContain("QA-NEXT-OWNER", oldList);
        var nextList = await nextOwner.GetStringAsync("/Workspace/Owner");
        Assert.Contains("QA-NEXT-OWNER", nextList);
        Assert.DoesNotContain("OSBB-0001", nextList);
        var oldReadings = WebUtility.HtmlDecode(await oldOwner.GetStringAsync("/OwnerAccounts/Readings/1"));
        Assert.Contains("Знайдено показань: 2", oldReadings);
        Assert.Contains(">104 м³</td>", oldReadings);
        Assert.Contains(">110 м³</td>", oldReadings);
        Assert.DoesNotContain(">112 м³</td>", oldReadings);
        var nextReadings = WebUtility.HtmlDecode(await nextOwner.GetStringAsync("/OwnerAccounts/Readings/3"));
        Assert.Contains("Знайдено показань: 1", nextReadings);
        Assert.Contains(">112 м³</td>", nextReadings);
        Assert.DoesNotContain(">110 м³</td>", nextReadings);
        Assert.DoesNotContain(">104 м³</td>", nextReadings);
        Assert.Contains("Знайдено показань: 0", WebUtility.HtmlDecode(await oldOwner.GetStringAsync("/OwnerAccounts/Readings/1?period=2026-12-01")));
        Assert.Contains("Знайдено показань: 0", WebUtility.HtmlDecode(await nextOwner.GetStringAsync("/OwnerAccounts/Readings/3?period=2026-11-01")));
        var payments = WebUtility.HtmlDecode(await oldOwner.GetStringAsync("/OwnerAccounts/Payments/1?dateFrom=2027-01-15&dateTo=2027-01-15"));
        Assert.Contains("Знайдено оплат: 1", payments);
        Assert.Contains("QA-OLD-LATE-PAYMENT", payments);
        payments = WebUtility.HtmlDecode(await nextOwner.GetStringAsync("/OwnerAccounts/Payments/3"));
        Assert.Contains("Знайдено оплат: 0", payments);
        Assert.DoesNotContain("QA-OLD-LATE-PAYMENT", payments);
        var charges = WebUtility.HtmlDecode(await nextOwner.GetStringAsync("/OwnerAccounts/Charges/3"));
        Assert.Contains("Знайдено нарахувань: 4", charges);
        Assert.Contains("12.2026", charges);
        Assert.DoesNotContain("11.2026", charges);
        Assert.Contains("Знайдено нарахувань: 0", WebUtility.HtmlDecode(await oldOwner.GetStringAsync("/OwnerAccounts/Charges/1?period=2026-12-01")));
        Assert.Matches("83[.,]75 грн", WebUtility.HtmlDecode(await oldOwner.GetStringAsync("/OwnerAccounts/Details/1")));
        Assert.Matches("636[.,]25 грн", WebUtility.HtmlDecode(await nextOwner.GetStringAsync("/OwnerAccounts/Details/3")));
        foreach (var action in new[] { "Details", "Charges", "Payments", "Readings" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await oldOwner.GetAsync($"/OwnerAccounts/{action}/3")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await nextOwner.GetAsync($"/OwnerAccounts/{action}/1")).StatusCode);
        }
        Assert.Equal(8, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Charges WHERE AccountId=1"));
        Assert.Equal(3281.25m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.Charges WHERE Period<'20261201'"));
    }
}
