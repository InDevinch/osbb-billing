using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class AccountsTests
{
    private static Dictionary<string, string> OpenFields() => new()
    {
        ["AccountNumber"] = "TEST-NEW", ["OwnerId"] = "1", ["PremiseId"] = "3",
        ["ValidFrom"] = "2026-01-01", ["ValidTo"] = ""
    };

    private static Dictionary<string, string> ChangeFields() => new()
    {
        ["AccountNumber"] = "TEST-NEXT", ["OwnerId"] = "2", ["ValidFrom"] = "2026-03-01"
    };

    private static async Task AddFreePremiseAsync(AuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        database.Premises.Add(new Premise { PremiseId = 3, BuildingId = 1, Number = "3", Kind = "Apartment", Area = 40 });
        await database.SaveChangesAsync();
    }

    [Fact]
    public async Task AccountCanOpenAtTheExactEndOfPreviousPeriod()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await AddFreePremiseAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = OpenFields();
        fields["ValidTo"] = "2026-03-01";
        fields["AccountId"] = "1";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Accounts/Create", fields)).StatusCode);
        fields = OpenFields();
        fields["AccountNumber"] = "TEST-ADJACENT";
        fields["OwnerId"] = "2";
        fields["ValidFrom"] = "2026-03-01";
        var response = await SubmitAsync(client, "/Accounts/Create", fields);
        Assert.Equal("/Accounts/Details/4", response.Headers.Location!.ToString());
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(4, await database.Accounts.CountAsync());
        Assert.Equal(1, (await database.Accounts.FindAsync(1))!.OwnerId);
        var next = await database.Accounts.FindAsync(4);
        Assert.Equal(2, next!.OwnerId);
        Assert.Equal(3, next.PremiseId);
        Assert.Null(next.ValidTo);
    }

    [Theory]
    [InlineData("AccountNumber", "")]
    [InlineData("AccountNumber", "Номер")]
    [InlineData("AccountNumber", "1234567890123456789012345678901")]
    [InlineData("OwnerId", "999")]
    [InlineData("PremiseId", "999")]
    [InlineData("ValidFrom", "")]
    [InlineData("ValidFrom", "2026-01-02")]
    [InlineData("ValidTo", "2026-02-02")]
    [InlineData("ValidTo", "2026-01-01")]
    [InlineData("ValidTo", "2025-12-01")]
    public async Task InvalidOpeningDoesNotCreateAccount(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await AddFreePremiseAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = OpenFields();
        fields[field] = value;
        var response = await SubmitAsync(client, "/Accounts/Create", fields);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", html);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Accounts.CountAsync());
    }

    [Fact]
    public async Task DuplicateNumbersAndOverlappingPeriodsAreRejected()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await AddFreePremiseAsync(factory);
        await LoginAsync(client, "accountant");
        var duplicate = OpenFields();
        duplicate["AccountNumber"] = "TEST-OWNER-1";
        var response = await SubmitAsync(client, "/Accounts/Create", duplicate);
        Assert.Contains("таким номером", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        var overlap = OpenFields();
        overlap["PremiseId"] = "1";
        response = await SubmitAsync(client, "/Accounts/Create", overlap);
        Assert.Contains("перетинається", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Accounts.CountAsync());
    }

    [Fact]
    public async Task ChangingPayerKeepsOldAccountAndFinancialLinksAndUpdatesOwnerVisibility()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Charges.Add(new Charge { ChargeId = 1, AccountId = 1, Period = new DateOnly(2026, 2, 1) });
            database.Payments.Add(new Payment { PaymentId = 1, AccountId = 1, Amount = 100, Reference = "TEST", PaymentDate = new DateOnly(2026, 4, 1) });
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var fields = ChangeFields();
        fields["PremiseId"] = "2";
        fields["id"] = "2";
        fields["CurrentAccount.OwnerId"] = "2";
        var response = await SubmitAsync(client, "/Accounts/ChangePayer/1", fields);
        Assert.Equal("/Accounts/Details/3", response.Headers.Location!.ToString());
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            var old = await database.Accounts.FindAsync(1);
            Assert.Equal(1, old!.OwnerId);
            Assert.Equal(new DateOnly(2026, 1, 1), old.ValidFrom);
            Assert.Equal(new DateOnly(2026, 3, 1), old.ValidTo);
            var next = await database.Accounts.FindAsync(3);
            Assert.Equal(1, next!.PremiseId);
            Assert.Equal(2, next.OwnerId);
            Assert.Equal(old.ValidTo, next.ValidFrom);
            Assert.Null(next.ValidTo);
            Assert.Equal(1, (await database.Charges.FindAsync(1))!.AccountId);
            Assert.Equal(1, (await database.Payments.FindAsync(1))!.AccountId);
        }
        var repeat = await SubmitAsync(client, "/Accounts/ChangePayer/1", ChangeFields());
        Assert.Contains("вже визначено завершення", WebUtility.HtmlDecode(await repeat.Content.ReadAsStringAsync()));
        using var firstOwner = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(firstOwner, "owner1");
        var firstHtml = await firstOwner.GetStringAsync("/Workspace/Owner");
        Assert.Contains("TEST-OWNER-1", firstHtml);
        Assert.DoesNotContain("TEST-NEXT", firstHtml);
        using var secondOwner = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(secondOwner, "owner2");
        var secondHtml = await secondOwner.GetStringAsync("/Workspace/Owner");
        Assert.Contains("TEST-NEXT", secondHtml);
        Assert.DoesNotContain("TEST-OWNER-1", secondHtml);
    }

    [Theory]
    [InlineData("OwnerId", "1")]
    [InlineData("OwnerId", "999")]
    [InlineData("AccountNumber", "TEST-OWNER-2")]
    [InlineData("ValidFrom", "2026-01-01")]
    [InlineData("ValidFrom", "2026-02-02")]
    [InlineData("ValidFrom", "2026-02-01")]
    public async Task InvalidChangeDoesNotClosePreviousAccount(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Charges.Add(new Charge { ChargeId = 1, AccountId = 1, Period = new DateOnly(2026, 2, 1) });
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var fields = ChangeFields();
        fields[field] = value;
        var response = await SubmitAsync(client, "/Accounts/ChangePayer/1", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        using var check = factory.Services.CreateScope();
        var databaseCheck = check.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Null((await databaseCheck.Accounts.FindAsync(1))!.ValidTo);
        Assert.Equal(2, await databaseCheck.Accounts.CountAsync());
    }

    [Theory]
    [InlineData("chair")]
    [InlineData("owner1")]
    [InlineData("")]
    public async Task OnlyAccountantCanOpenAccountsOrChangePayer(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        if (login != "") await LoginAsync(client, login);
        foreach (var path in new[] { "/Accounts", "/Accounts/Details/1" })
        {
            var response = await client.GetAsync(path);
            if (login == "chair")
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain("Змінити платника", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
            }
            else Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/Accounts/Create", "/Accounts/ChangePayer/1" })
        {
            var get = await client.GetAsync(path);
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", get.Headers.Location!.ToString());
            var post = await client.PostAsync(path, new FormUrlEncodedContent(ChangeFields()));
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", post.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task WritesRequireAntiforgeryAndUnknownIdsReturn404()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        foreach (var path in new[] { "/Accounts/Create", "/Accounts/ChangePayer/1" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsync(path, new FormUrlEncodedContent(OpenFields()))).StatusCode);
        foreach (var path in new[] { "/Accounts/Details/999", "/Accounts/ChangePayer/999",
                     "/Accounts?ownerId=999", "/Accounts?premiseId=999", "/Accounts/Create?premiseId=999" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task SearchAndStatusFiltersPreservePagination()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await AddFreePremiseAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            var now = DateTime.Today;
            var month = new DateOnly(now.Year, now.Month, 1);
            database.Accounts.AddRange(Enumerable.Range(1, 25).Select(i => new Account
            {
                AccountNumber = $"PAGE-{i:D2}", OwnerId = 1, PremiseId = 3,
                ValidFrom = month.AddMonths(-30 + i), ValidTo = month.AddMonths(-29 + i)
            }));
            database.Accounts.Add(new Account { AccountNumber = "FUTURE", OwnerId = 1, PremiseId = 3, ValidFrom = month.AddMonths(1) });
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var first = await client.GetStringAsync("/Accounts?ownerId=1&premiseId=3&search=PAGE&status=closed");
        Assert.Contains(">PAGE-01</a>", first);
        Assert.Contains(">PAGE-20</a>", first);
        Assert.DoesNotContain(">PAGE-21</a>", first);
        Assert.Contains("ownerId=1", first);
        Assert.Contains("premiseId=3", first);
        Assert.Contains("status=closed", first);
        var last = await client.GetStringAsync("/Accounts?ownerId=1&premiseId=3&search=PAGE&status=closed&page=2147483647");
        Assert.Contains(">PAGE-25</a>", last);
        Assert.DoesNotContain(">PAGE-20</a>", last);
        var future = await client.GetStringAsync("/Accounts?ownerId=1&premiseId=3&status=planned");
        Assert.Contains(">FUTURE</a>", future);
        Assert.DoesNotContain(">PAGE-01</a>", future);
    }
}
