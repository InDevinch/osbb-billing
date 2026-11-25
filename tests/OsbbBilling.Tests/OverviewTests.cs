using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Services;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class OverviewTests
{
    [Theory]
    [InlineData("chair")]
    [InlineData("accountant")]
    public async Task StaffCanReadOverviewAndDebtors(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, login);
        foreach (var path in new[] { "/Overview", "/Overview/Debtors" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        if (login == "chair")
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Workspace/Chairperson"));
            Assert.Contains("Фінансовий огляд ОСББ", html);
            Assert.Contains("action=\"/Overview\"", html);
            Assert.Contains("href=\"/Overview/Debtors\"", html);
            Assert.DoesNotContain("/Payments/Create", html);
        }
    }

    [Theory]
    [InlineData("owner1")]
    [InlineData("owner2")]
    public async Task OwnersAndAnonymousUsersCannotReadOverallFinancialData(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        foreach (var path in new[] { "/Overview", "/Overview/Debtors" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
        await LoginAsync(client, login);
        foreach (var path in new[] { "/Overview?ownerId=1", "/Overview/Debtors?buildingId=1", "/Workspace/Chairperson" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task ChairCannotUseWriteEndpointsAndOverviewHasNoPostAction()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "chair");
        var token = await TokenAsync(client, "/Overview");
        foreach (var path in new[] { "/Payments/Create", "/Charges/Generate", "/Meters/Create", "/Accounts/Create" })
        {
            var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/Overview", "/Overview/Debtors" })
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(path,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }))).StatusCode);
    }

    [Fact]
    public async Task OverviewCountsAccountsAndHandlesBuildingWithoutAccounts()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Buildings.Add(new Building { BuildingId = 2, Address = "Порожній будинок" });
            database.Payments.AddRange(
                new Payment { PaymentId = 1, AccountId = 1, Amount = 30.25m, PaymentDate = new DateOnly(2026, 11, 1), Reference = "TEST-A", CreatedBy = 1 },
                new Payment { PaymentId = 2, AccountId = 2, Amount = 40.75m, PaymentDate = new DateOnly(2026, 11, 1), Reference = "TEST-B", CreatedBy = 1 });
            await database.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FinancialOverviewService>();
            var overview = await service.GetAsync(null, default);
            Assert.Equal(2, overview.AccountCount);
            Assert.Equal(2, overview.CreditAccountCount);
            Assert.Equal(0, overview.DebtorCount);
            Assert.Equal(0m, overview.TotalCharged);
            Assert.Equal(0m, overview.TotalDebt);
            Assert.Equal(71m, overview.TotalPaid);
            Assert.Equal(71m, overview.TotalCredit);
            var empty = await service.GetAsync(2, default);
            Assert.Equal(0, empty.AccountCount);
            Assert.Equal(0m, empty.TotalPaid + empty.TotalCharged + empty.TotalDebt + empty.TotalCredit);
        }
        await LoginAsync(client, "chair");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview?buildingId=2"));
        Assert.Contains("buildingId=2", html);
        Assert.Contains("0.00 грн", html.Replace(',', '.'));
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Overview/Debtors?minimumDebt=0"));
        Assert.Contains("Рахунків із боргом за вибіркою: 0", html);
        Assert.DoesNotContain("TEST-OWNER-1", html);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("100000000000000000")]
    [InlineData("bad")]
    public async Task InvalidMinimumDebtIsShownAsValidationError(string minimumDebt)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "chair");
        var html = await client.GetStringAsync("/Overview/Debtors?minimumDebt=" + minimumDebt);
        Assert.Contains("field-validation-error", html);
        Assert.Contains("Рахунків із боргом за вибіркою: 0", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task UnknownBuildingsAndMalformedFiltersDoNotShowUnfilteredOverview()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "chair");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Overview?buildingId=999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Overview/Debtors?buildingId=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Overview?buildingId=bad")).StatusCode);
        Assert.Contains("field-validation-error", await client.GetStringAsync("/Overview/Debtors?buildingId=bad"));
        var html = await client.GetStringAsync("/Overview/Debtors?search=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }
}
