using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Services;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class BalanceSqlTests : SqlDatabaseTestBase
{
    private async Task AssertMatchesSqlViewAsync(AccountBalanceViewModel balance)
    {
        var id = balance.AccountId;
        Assert.Equal(await ScalarAsync<decimal>($"SELECT TotalCharged FROM dbo.v_AccountBalances WHERE AccountId={id}"), balance.TotalCharged);
        Assert.Equal(await ScalarAsync<decimal>($"SELECT TotalPaid FROM dbo.v_AccountBalances WHERE AccountId={id}"), balance.TotalPaid);
        Assert.Equal(await ScalarAsync<decimal>($"SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId={id}"), balance.DebtAmount);
        Assert.Equal(await ScalarAsync<decimal>($"SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId={id}"), balance.CreditAmount);
        Assert.Equal(await ScalarAsync<decimal>($"SELECT NetBalance FROM dbo.v_AccountBalances WHERE AccountId={id}"), balance.NetBalance);
    }

    private static void AssertMoney(string html, string label, string amount) =>
        Assert.Matches($"{Regex.Escape(label)}</dt><dd[^>]*>{Regex.Escape(amount)} грн</dd>", html.Replace(',', '.'));

    [SqlServerWriteFact]
    public async Task DemoBalancesMatchSqlViewAndAccountPages()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<AccountBalanceService>();
        var rows = await service.Query().OrderBy(x => x.AccountId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1426.25m, rows[0].TotalCharged);
        Assert.Equal(1500m, rows[0].TotalPaid);
        Assert.Equal(0m, rows[0].DebtAmount);
        Assert.Equal(73.75m, rows[0].CreditAmount);
        Assert.Equal(-73.75m, rows[0].NetBalance);
        Assert.Equal(1855m, rows[1].TotalCharged);
        Assert.Equal(400m, rows[1].TotalPaid);
        Assert.Equal(1455m, rows[1].DebtAmount);
        foreach (var balance in rows) await AssertMatchesSqlViewAsync(balance);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<OsbbDbContext>().ChangeTracker.Entries());
        using var client = Client();
        await LoginAsync(client, "chair", "OsbbDemo2026!");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Accounts/Details/1"));
        AssertMoney(html, "Нараховано", "1426.25");
        AssertMoney(html, "Сплачено", "1500.00");
        AssertMoney(html, "Борг", "0.00");
        AssertMoney(html, "Аванс", "73.75");
        AssertMoney(html, "Сальдо", "-73.75");
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Balances?buildingId=1"));
        Assert.Contains("OSBB-0001", html);
        Assert.Contains("OSBB-0002", html);
        Assert.Contains("Знайдено рахунків: 2", html);
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task PartialPaymentUpdatesBalanceAndChargePaymentDetailsWithoutDoubleCounting()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Payments/Create", new()
        {
            ["AccountId"] = "2", ["PaymentDate"] = "2026-12-15", ["Amount"] = "500.25", ["Reference"] = "QA-BALANCE"
        })).StatusCode);
        using var scope = Scope();
        var balance = await scope.ServiceProvider.GetRequiredService<AccountBalanceService>().FindAsync(2, default);
        Assert.NotNull(balance);
        Assert.Equal(1855m, balance.TotalCharged);
        Assert.Equal(900.25m, balance.TotalPaid);
        Assert.Equal(954.75m, balance.DebtAmount);
        Assert.Equal(0m, balance.CreditAmount);
        await AssertMatchesSqlViewAsync(balance);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Accounts/Details/2"));
        AssertMoney(html, "Борг", "954.75");
        var chargeId = await ScalarAsync<int>("""
            SELECT TOP(1) c.ChargeId FROM dbo.Charges c JOIN dbo.PaymentAllocations a ON a.ChargeId=c.ChargeId
            WHERE c.AccountId=2 GROUP BY c.ChargeId HAVING COUNT(*)=2 ORDER BY c.ChargeId
            """);
        html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Charges/Details/{chargeId}"));
        AssertMoney(html, "Сплачено", "560.00");
        AssertMoney(html, "Залишок боргу", "0.00");
        Assert.Contains("DEMO-PAY-002", html);
        Assert.Contains("QA-BALANCE", html);
        var allocations = await ScalarAsync<decimal>($"SELECT PaidAmount FROM dbo.v_ChargeBalances WHERE ChargeId={chargeId}");
        Assert.Equal(560m, allocations);
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task NewPayerHasZeroBalanceAndOldDebtOrCreditStaysOnOldAccount()
    {
        await ExecuteAsync("""
            UPDATE dbo.Accounts SET ValidTo='20261201' WHERE AccountId=2;
            INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES(N'QA-NEW-BALANCE',1,2,'20261201');
            """);
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<AccountBalanceService>();
        var old = await service.FindAsync(2, default);
        var next = await service.FindAsync(3, default);
        Assert.NotNull(old);
        Assert.NotNull(next);
        Assert.Equal(1455m, old.DebtAmount);
        Assert.Equal(0m, next.TotalCharged + next.TotalPaid + next.DebtAmount + next.CreditAmount);
        await AssertMatchesSqlViewAsync(next);
        await ExecuteAsync("EXEC dbo.usp_RegisterPayment 2,'20261215',2000,N'QA-OLD-CREDIT',1");
        old = await service.FindAsync(2, default);
        next = await service.FindAsync(3, default);
        Assert.Equal(545m, old!.CreditAmount);
        Assert.Equal(0m, next!.CreditAmount);
        await AssertMatchesSqlViewAsync(old);
        using var client = Client();
        await LoginAsync(client, "chair", "OsbbDemo2026!");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Balances?search=OSBB-0002"));
        Assert.Contains("Знайдено рахунків: 1", html);
        Assert.Matches("545[.,]00", html);
        Assert.DoesNotContain("QA-NEW-BALANCE", html);
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task UnallocatedCreditDoesNotHideDebtUntilApplied()
    {
        await ExecuteAsync("""
            INSERT dbo.Payments(AccountId,PaymentDate,Amount,Reference,CreatedBy)
            VALUES(2,'20261215',100,N'QA-UNALLOCATED',1);
            """);
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<AccountBalanceService>();
        var before = await service.FindAsync(2, default);
        Assert.NotNull(before);
        Assert.Equal(1455m, before.DebtAmount);
        Assert.Equal(100m, before.CreditAmount);
        Assert.Equal(1355m, before.NetBalance);
        await AssertMatchesSqlViewAsync(before);
        await ExecuteAsync("EXEC dbo.usp_ApplyPayments 2");
        var after = await service.FindAsync(2, default);
        Assert.NotNull(after);
        Assert.Equal(1355m, after.DebtAmount);
        Assert.Equal(0m, after.CreditAmount);
        Assert.Equal(before.NetBalance, after.NetBalance);
        await AssertMatchesSqlViewAsync(after);
        await AssertFinancialHistoryAsync();
    }
}
