using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class PaymentSqlTests : SqlDatabaseTestBase
{
    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string reference, string amount = "500.25", int accountId = 2) =>
        SubmitAsync(client, "/Payments/Create", new()
        {
            ["AccountId"] = accountId.ToString(), ["PaymentDate"] = "2026-12-15", ["Amount"] = amount,
            ["Reference"] = reference, ["CreatedBy"] = "2", ["PaymentId"] = "1"
        });

    [SqlServerWriteFact]
    public async Task PartialPaymentPaysOldestDebtAndUsesAuthenticatedAuthor()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(client, " QA-PARTIAL ")).StatusCode);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Reference=N'QA-PARTIAL' AND AccountId=2 AND Amount=500.25 AND CreatedBy=1 AND PaymentDate='20261215'"));
        Assert.Equal(480m, await ScalarAsync<decimal>("""
            SELECT SUM(a.Amount) FROM dbo.PaymentAllocations a JOIN dbo.Payments p ON p.PaymentId=a.PaymentId
            JOIN dbo.Charges c ON c.ChargeId=a.ChargeId WHERE p.Reference=N'QA-PARTIAL' AND c.Period='20261001'
            """));
        Assert.Equal(20.25m, await ScalarAsync<decimal>("""
            SELECT SUM(a.Amount) FROM dbo.PaymentAllocations a JOIN dbo.Payments p ON p.PaymentId=a.PaymentId
            JOIN dbo.Charges c ON c.ChargeId=a.ChargeId WHERE p.Reference=N'QA-PARTIAL' AND c.Period='20261101'
            """));
        Assert.Equal(954.75m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        var id = await ScalarAsync<int>("SELECT PaymentId FROM dbo.Payments WHERE Reference=N'QA-PARTIAL'");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Payments/Details/{id}"));
        Assert.Contains("10.2026", html);
        Assert.Contains("11.2026", html);
        Assert.Contains("accountant", html);
        Assert.Matches("500[.,]25 грн", html);
        Assert.Contains("Знайдено: 1", WebUtility.HtmlDecode(await client.GetStringAsync("/Payments?accountId=2&dateFrom=2026-12-15&dateTo=2026-12-15&search=QA-PARTIAL")));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task ExcessPaymentBecomesCreditAndIsAppliedToNextCharges()
    {
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(client, "QA-ADVANCE", "2000")).StatusCode);
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        Assert.Equal(545m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        var id = await ScalarAsync<int>("SELECT PaymentId FROM dbo.Payments WHERE Reference=N'QA-ADVANCE'");
        Assert.Matches("545[.,]00 грн", WebUtility.HtmlDecode(await client.GetStringAsync($"/Payments/Details/{id}")));
        await ExecuteAsync("""
            INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue)
            VALUES(1,'20261201','20261231',112),(2,'20261201','20261231',215);
            """);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Charges/Generate", new() { ["Period"] = "2026-12-01" })).StatusCode);
        Assert.Equal(310m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
    }

    [SqlServerWriteFact]
    public async Task ClosedAccountAcceptsOldDebtPaymentWithoutTransferringItToNewPayer()
    {
        await ExecuteAsync("""
            UPDATE dbo.Accounts SET ValidTo='20261201' WHERE AccountId=2;
            INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES(N'QA-NEW-PAYER',1,2,'20261201');
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var form = WebUtility.HtmlDecode(await client.GetStringAsync("/Payments/Create?accountId=2"));
        Assert.Contains("OSBB-0002", form);
        Assert.Contains("selected=\"selected\" value=\"2\"", form);
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(client, "QA-CLOSED", "1455")).StatusCode);
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE AccountId=3"));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=3"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task DuplicateDocumentIsRejectedAcrossAccountsAndConcurrentRequests()
    {
        using var first = Client();
        using var second = Client();
        await LoginAsync(first, "accountant", "OsbbDemo2026!");
        await LoginAsync(second, "accountant", "OsbbDemo2026!");
        var responses = await Task.WhenAll(RegisterAsync(first, "QA-DUP", "200"), RegisterAsync(second, "QA-DUP", "200"));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Redirect);
        var rejected = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Contains("вже зареєстровано", WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()));
        var retry = await RegisterAsync(first, " qa-dup ", "999", 1);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Contains("вже зареєстровано", WebUtility.HtmlDecode(await retry.Content.ReadAsStringAsync()));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Reference=N'QA-DUP'"));
        Assert.Equal(1255m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task ConcurrentDifferentPaymentsDoNotOverpayCharges()
    {
        using var first = Client();
        using var second = Client();
        await LoginAsync(first, "accountant", "OsbbDemo2026!");
        await LoginAsync(second, "accountant", "OsbbDemo2026!");
        var responses = await Task.WhenAll(RegisterAsync(first, "QA-CONCURRENT-A", "1000"),
            RegisterAsync(second, "QA-CONCURRENT-B", "1000"));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Reference LIKE N'QA-CONCURRENT-%'"));
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        Assert.Equal(545m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.v_ChargeBalances WHERE DebtAmount<0"));
        await AssertFinancialHistoryAsync();
    }

    [SqlServerWriteFact]
    public async Task AllocationFailureRollsBackPaymentAndAllowsSafeRetry()
    {
        await ExecuteAsync("""
            CREATE TRIGGER dbo.tr_QaPaymentFailure ON dbo.PaymentAllocations AFTER INSERT AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.Payments p ON p.PaymentId=i.PaymentId WHERE p.Reference=N'QA-FAIL')
                    THROW 52014,N'Test payment allocation failure',1;
            END
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        var response = await RegisterAsync(client, "QA-FAIL");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Перевірте список", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Reference=N'QA-FAIL'"));
        Assert.Equal(1455m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
        await AssertFinancialHistoryAsync();
        await ExecuteAsync("DROP TRIGGER dbo.tr_QaPaymentFailure");
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(client, "QA-FAIL")).StatusCode);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Reference=N'QA-FAIL'"));
        Assert.Equal(954.75m, await ScalarAsync<decimal>("SELECT DebtAmount FROM dbo.v_AccountBalances WHERE AccountId=2"));
    }

    [SqlServerWriteFact]
    public async Task PaymentWithoutChargesIsEntirelyCreditAndCannotBeEditedOrDeleted()
    {
        await ExecuteAsync("""
            INSERT dbo.Premises(BuildingId,Number,Kind,Area,ResidentsCount) VALUES(1,N'QA-3','Apartment',40,1);
            INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES(N'QA-NO-CHARGES',1,3,'20261201');
            """);
        using var client = Client();
        await LoginAsync(client, "accountant", "OsbbDemo2026!");
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(client, "QA-ONLY-CREDIT", "10.01", 3)).StatusCode);
        Assert.Equal(10.01m, await ScalarAsync<decimal>("SELECT CreditAmount FROM dbo.v_AccountBalances WHERE AccountId=3"));
        var id = await ScalarAsync<int>("SELECT PaymentId FROM dbo.Payments WHERE Reference=N'QA-ONLY-CREDIT'");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Payments/Details/{id}"));
        Assert.Contains("Оплата збережена як аванс", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Payments/Edit/{id}")).StatusCode);
        var token = await TokenAsync(client, "/Payments");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync($"/Payments/Delete/{id}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }))).StatusCode);
        await AssertFinancialHistoryAsync();
    }
}
