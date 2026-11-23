using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Services;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class BalanceTests
{
    [Theory]
    [InlineData("accountant")]
    [InlineData("chair")]
    public async Task StaffCanReadBalancesAndOpenAccountDetails(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, login);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Balances"));
        Assert.Contains("Знайдено рахунків: 2", html);
        Assert.Contains("href=\"/Accounts/Details/1\"", html);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Accounts/Details/1"));
        Assert.Contains("Поточний баланс", html);
        Assert.Contains("Нараховано", html);
        Assert.Contains("Аванс", html);
        Assert.Contains("href=\"/Charges?accountId=1\"", html);
        Assert.Contains("href=\"/Payments?accountId=1\"", html);
    }

    [Theory]
    [InlineData("owner1")]
    [InlineData("owner2")]
    public async Task OwnersCannotReadStaffBalancesOrAccountDetails(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var anonymous = await client.GetAsync("/Balances");
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
        await LoginAsync(client, login);
        foreach (var path in new[] { "/Balances", "/Balances?buildingId=1", "/Accounts/Details/1", "/Accounts/Details/2" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task EmptyAccountHasZeroBalanceAndPaymentWithoutChargesIsCredit()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Payments.Add(new Payment
            {
                PaymentId = 1, AccountId = 1, PaymentDate = new DateOnly(2026, 11, 20),
                Amount = 100.25m, Reference = "TEST-ADVANCE", CreatedBy = 1
            });
            await database.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AccountBalanceService>();
            var advance = await service.FindAsync(1, default);
            Assert.NotNull(advance);
            Assert.Equal(0m, advance.TotalCharged);
            Assert.Equal(100.25m, advance.TotalPaid);
            Assert.Equal(0m, advance.DebtAmount);
            Assert.Equal(100.25m, advance.CreditAmount);
            Assert.Equal(-100.25m, advance.NetBalance);
            var empty = await service.FindAsync(2, default);
            Assert.NotNull(empty);
            Assert.Equal(0m, empty.TotalCharged + empty.TotalPaid + empty.DebtAmount + empty.CreditAmount + empty.NetBalance);
            Assert.Null(await service.FindAsync(999, default));
            Assert.Empty(scope.ServiceProvider.GetRequiredService<OsbbDbContext>().ChangeTracker.Entries());
        }
        await LoginAsync(client, "accountant");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Accounts/Details/1"));
        Assert.Matches("100[.,]25 грн", html);
        Assert.Matches("-100[.,]25 грн", html);
    }

    [Fact]
    public async Task SearchBuildingFilterAndPaginationPreserveClosedAccountsAndEncodeNames()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            (await database.Owners.FindAsync(1))!.FullName = "<script>alert(1)</script>";
            database.Buildings.Add(new Building { BuildingId = 2, Address = "Другий будинок" });
            database.Premises.Add(new Premise { PremiseId = 3, BuildingId = 2, Number = "3", Kind = "Apartment", Area = 40 });
            for (var id = 100; id <= 120; id++)
            {
                var from = new DateOnly(2000, 1, 1).AddMonths(id - 100);
                database.Accounts.Add(new Account
                {
                    AccountId = id, AccountNumber = "PAGE-" + id, OwnerId = 1, PremiseId = 3,
                    ValidFrom = from, ValidTo = from.AddMonths(1)
                });
            }
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "chair");
        var html = await client.GetStringAsync("/Balances?buildingId=2&search=PAGE&page=2");
        Assert.Contains("Знайдено рахунків: 21", WebUtility.HtmlDecode(html));
        Assert.Contains("Сторінка 2 з 2", WebUtility.HtmlDecode(html));
        Assert.Contains("buildingId=2", html);
        Assert.Contains("search=PAGE", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("TEST-OWNER-2", html);
        Assert.Contains("Знайдено рахунків: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Balances?buildingId=1&search=PAGE")));
        Assert.Contains("Знайдено рахунків: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Balances?buildingId=bad")));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Balances?buildingId=999")).StatusCode);
    }
}
