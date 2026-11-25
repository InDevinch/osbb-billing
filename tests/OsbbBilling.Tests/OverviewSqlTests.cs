using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Services;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class OverviewSqlTests : SqlDatabaseTestBase
{
    private static void AssertMoney(string html, string label, string amount) =>
        Assert.Matches($"{Regex.Escape(label)}</dt><dd[^>]*>{Regex.Escape(amount)} грн</dd>", html.Replace(',', '.'));

    [SqlServerWriteFact]
    public async Task OverviewKeepsDebtSeparateFromCreditAndUpdatesAfterPayment()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<FinancialOverviewService>();
        var overview = await service.GetAsync(null, default);
        Assert.Equal(3281.25m, overview.TotalCharged);
        Assert.Equal(1900m, overview.TotalPaid);
        Assert.Equal(1455m, overview.TotalDebt);
        Assert.Equal(73.75m, overview.TotalCredit);
        Assert.Equal(2, overview.AccountCount);
        Assert.Equal(1, overview.DebtorCount);
        Assert.Equal(1, overview.CreditAccountCount);
        Assert.Equal(await ScalarAsync<decimal>("SELECT SUM(DebtAmount) FROM dbo.v_AccountBalances"), overview.TotalDebt);
        Assert.Equal(await ScalarAsync<decimal>("SELECT SUM(CreditAmount) FROM dbo.v_AccountBalances"), overview.TotalCredit);
        using var client = Client();
        await LoginAsync(client, "chair", "OsbbDemo2026!");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Workspace/Chairperson"));
        AssertMoney(html, "Нараховано", "3281.25");
        AssertMoney(html, "Сплачено", "1900.00");
        AssertMoney(html, "Загальний борг", "1455.00");
        AssertMoney(html, "Аванси", "73.75");
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview/Debtors?minimumDebt=1455"));
        Assert.Contains("OSBB-0002", html);
        Assert.DoesNotContain("OSBB-0001", html);
        Assert.Contains("href=\"/Accounts/Details/2\"", html);
        Assert.Contains("Рахунків із боргом за вибіркою: 1", html);
        Assert.Contains("Рахунків із боргом за вибіркою: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Overview/Debtors?minimumDebt=1455.01")));
        await ExecuteAsync("EXEC dbo.usp_RegisterPayment 2,'20261215',500.25,N'QA-OVERVIEW-PAYMENT',1");
        overview = await service.GetAsync(null, default);
        Assert.Equal(2400.25m, overview.TotalPaid);
        Assert.Equal(954.75m, overview.TotalDebt);
        Assert.Equal(73.75m, overview.TotalCredit);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview"));
        AssertMoney(html, "Загальний борг", "954.75");
        AssertMoney(html, "Сплачено", "2400.25");
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task BuildingSearchMinimumDebtAndPaginationUseAllFilteredRowsAndKeepClosedDebt()
    {
        await ExecuteAsync("""
            UPDATE dbo.Accounts SET ValidTo='20261201' WHERE AccountId=2;
            INSERT dbo.Buildings(Address) VALUES(N'QA-Другий будинок');
            INSERT dbo.Owners(FullName) VALUES(N'<script>alert(1)</script>');
            INSERT dbo.Tariffs(BuildingId,ServiceId,UnitPrice,ValidFrom) VALUES(2,4,100,'20260101');
            DECLARE @tariff int=CONVERT(int,SCOPE_IDENTITY()),@i int=1,@premise int,@account int,@service int;
            WHILE @i<=21
            BEGIN
                INSERT dbo.Premises(BuildingId,Number,Kind,Area,ResidentsCount) VALUES(2,CONVERT(nvarchar(10),@i),'Apartment',40,1);
                SET @premise=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom)
                    VALUES(N'QA-DEBT-'+RIGHT(N'000'+CONVERT(nvarchar(3),@i),3),3,@premise,'20260101');
                SET @account=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.PremiseServices(PremiseId,ServiceId,ValidFrom) VALUES(@premise,4,'20260101');
                SET @service=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.Charges(AccountId,PremiseServiceId,TariffId,Period,Quantity,UnitPrice,CreatedBy)
                    VALUES(@account,@service,@tariff,'20261201',1,100,1);
                SET @i+=1;
            END;
            """);
        using var client = Client();
        await LoginAsync(client, "chair", "OsbbDemo2026!");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview?buildingId=2"));
        AssertMoney(html, "Нараховано", "2100.00");
        AssertMoney(html, "Сплачено", "0.00");
        AssertMoney(html, "Загальний борг", "2100.00");
        AssertMoney(html, "Аванси", "0.00");
        Assert.Contains("/Overview/Debtors?buildingId=2", html);
        html = await client.GetStringAsync("/Overview/Debtors?buildingId=2&search=QA-DEBT&minimumDebt=100&page=2");
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Рахунків із боргом за вибіркою: 21", decoded);
        Assert.Contains("Загальний борг за вибіркою: 2100.00 грн", decoded.Replace(',', '.'));
        Assert.Contains("Сторінка 2 з 2", decoded);
        Assert.Contains("QA-DEBT-021", html);
        Assert.DoesNotContain("QA-DEBT-001", html);
        Assert.DoesNotContain("OSBB-0002", html);
        Assert.Contains("buildingId=2", html);
        Assert.Contains("minimumDebt=100", html);
        Assert.Contains("search=QA-DEBT", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        decoded = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview/Debtors?buildingId=2&minimumDebt=100.01"));
        Assert.Contains("Рахунків із боргом за вибіркою: 0", decoded);
        decoded = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview/Debtors?buildingId=1&search=OSBB-0002"));
        Assert.Contains("Рахунків із боргом за вибіркою: 1", decoded);
        Assert.Contains("OSBB-0002", decoded);
        Assert.Contains("1455.00", decoded.Replace(',', '.'));
        using var scope = Scope();
        var all = await scope.ServiceProvider.GetRequiredService<FinancialOverviewService>().GetAsync(null, default);
        Assert.Equal(3555m, all.TotalDebt);
        Assert.Equal(73.75m, all.TotalCredit);
        Assert.Equal(22, all.DebtorCount);
        Assert.Equal(await ScalarAsync<decimal>("SELECT SUM(DebtAmount) FROM dbo.v_AccountBalances"), all.TotalDebt);
    }
}
