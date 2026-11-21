using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

[Trait("Category", "Integration")]
[Trait("Database", "SqlWrite")]
public class AccountSqlTests : SqlDatabaseTestBase
{
    private static Dictionary<string, string> ChangeFields(string number) => new()
    {
        ["OwnerId"] = "2", ["AccountNumber"] = number, ["ValidFrom"] = "2026-12-01"
    };

    [SqlServerWriteFact]
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

    [SqlServerWriteFact]
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

    [SqlServerWriteFact]
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

    [SqlServerWriteFact]
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

}
