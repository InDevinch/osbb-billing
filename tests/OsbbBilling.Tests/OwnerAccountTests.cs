using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class OwnerAccountTests
{
    private static readonly string[] Pages = ["Details", "Charges", "Payments", "Readings"];

    private static async Task SeedLedgerAsync(AuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        for (var id = 1; id <= 2; id++)
        {
            database.Services.Add(new Service { ServiceId = id, Name = "OWNER-SERVICE-" + id, Unit = "м³", CalculationMethod = "Meter" });
            database.PremiseServices.Add(new PremiseService { PremiseServiceId = id, PremiseId = id, ServiceId = id, ValidFrom = new DateOnly(2026, 1, 1) });
            database.Tariffs.Add(new Tariff { TariffId = id, BuildingId = 1, ServiceId = id, UnitPrice = 0, ValidFrom = new DateOnly(2026, 1, 1) });
            database.Meters.Add(new Meter { MeterId = id, PremiseServiceId = id, SerialNumber = "OWNER-METER-" + id, InstalledOn = new DateOnly(2026, 11, 1) });
            database.MeterReadings.Add(new MeterReading { MeterReadingId = id, MeterId = id, Period = new DateOnly(2026, 11, 1), ReadingDate = new DateOnly(2026, 11, 25), ReadingValue = id * 10 + 0.125m });
            database.Charges.Add(new Charge { ChargeId = id, AccountId = id, PremiseServiceId = id, TariffId = id, Period = new DateOnly(2026, 11, 1), Quantity = id * 10, UnitPrice = 0, CreatedBy = 1 });
            database.Payments.Add(new Payment { PaymentId = id, AccountId = id, PaymentDate = new DateOnly(2026, 11, 20), Amount = id * 25.50m, Reference = "OWNER-PAY-" + id, CreatedBy = 1 });
        }
        await database.SaveChangesAsync();
    }

    [Theory]
    [InlineData("owner1", 1, 2)]
    [InlineData("owner2", 2, 1)]
    public async Task EveryPageUsesSessionOwnerAndRejectsOtherAccounts(string login, int ownId, int otherId)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedLedgerAsync(factory);
        await LoginAsync(client, login);
        var list = await client.GetStringAsync($"/Workspace/Owner?ownerId={otherId}");
        Assert.Contains($"href=\"/OwnerAccounts/Details/{ownId}\"", list);
        Assert.DoesNotContain($"/OwnerAccounts/Details/{otherId}", list);
        foreach (var action in Pages)
        {
            var response = await client.GetAsync($"/OwnerAccounts/{action}/{ownId}?id={otherId}&accountId={otherId}&ownerId={otherId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("TEST-OWNER-" + ownId, html);
            Assert.DoesNotContain("TEST-OWNER-" + otherId, html);
            Assert.DoesNotContain("OWNER-SERVICE-" + otherId, html);
            Assert.DoesNotContain("OWNER-PAY-" + otherId, html);
            Assert.DoesNotContain("OWNER-METER-" + otherId, html);
            Assert.DoesNotContain("/Payments/Create", html);
            Assert.DoesNotContain("/Charges/Generate", html);
            Assert.DoesNotContain("/MeterReadings/Create", html);
            Assert.DoesNotContain("/Accounts/Details/", html);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/OwnerAccounts/{action}/{otherId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/OwnerAccounts/{action}/999")).StatusCode);
        }
        Assert.Contains("OWNER-SERVICE-" + ownId, await client.GetStringAsync($"/OwnerAccounts/Charges/{ownId}"));
        Assert.Contains("OWNER-PAY-" + ownId, await client.GetStringAsync($"/OwnerAccounts/Payments/{ownId}"));
        Assert.Contains("OWNER-METER-" + ownId, await client.GetStringAsync($"/OwnerAccounts/Readings/{ownId}"));
    }

    [Theory]
    [InlineData("accountant")]
    [InlineData("chair")]
    public async Task OwnerPagesRequireOwnerRole(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        foreach (var action in Pages)
        {
            var response = await client.GetAsync($"/OwnerAccounts/{action}/1");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
        await LoginAsync(client, login);
        foreach (var action in Pages)
        {
            var response = await client.GetAsync($"/OwnerAccounts/{action}/1");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task OwnerPagesAreReadOnlyAndStaffWriteEndpointsRemainForbidden()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedLedgerAsync(factory);
        await LoginAsync(client, "owner1");
        var token = await TokenAsync(client, "/OwnerAccounts/Details/1");
        foreach (var action in Pages)
        {
            var response = await client.PostAsync($"/OwnerAccounts/{action}/1", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["AccountId"] = "2", ["OwnerId"] = "2", ["Amount"] = "999"
            }));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
        foreach (var path in new[] { "/Payments/Create", "/Charges/Generate", "/MeterReadings/Create" })
        {
            var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(2, await database.Payments.CountAsync());
        Assert.Equal(2, await database.Charges.CountAsync());
        Assert.Equal(2, await database.MeterReadings.CountAsync());
        Assert.Equal(25.50m, (await database.Payments.FindAsync(1))!.Amount);
    }

    [Theory]
    [InlineData("Charges", "period=2026-11-02")]
    [InlineData("Charges", "period=bad")]
    [InlineData("Readings", "period=2026-11-02")]
    [InlineData("Readings", "period=bad")]
    [InlineData("Payments", "dateFrom=2026-11-21&dateTo=2026-11-20")]
    [InlineData("Payments", "dateFrom=bad")]
    public async Task InvalidDateFiltersDoNotShowUnfilteredData(string action, string query)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedLedgerAsync(factory);
        await LoginAsync(client, "owner1");
        var html = await client.GetStringAsync($"/OwnerAccounts/{action}/1?{query}");
        Assert.Contains("field-validation-error", html);
        Assert.DoesNotContain("OWNER-SERVICE-1", html);
        Assert.DoesNotContain("OWNER-PAY-1", html);
        Assert.DoesNotContain("OWNER-METER-1", html);
    }

    [Fact]
    public async Task PaginationAndDateFiltersStayWithinAccountAndEncodePaymentReferences()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedLedgerAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            (await database.Payments.FindAsync(1))!.Reference = "<script>alert(1)</script>";
            for (var id = 100; id <= 120; id++)
            {
                var period = new DateOnly(2026, 12, 1).AddMonths(id - 100);
                database.Charges.Add(new Charge { ChargeId = id, AccountId = 1, PremiseServiceId = 1, TariffId = 1, Period = period, Quantity = 1, UnitPrice = 0, CreatedBy = 1 });
                database.Payments.Add(new Payment { PaymentId = id, AccountId = 1, PaymentDate = new DateOnly(2026, 11, 25), Amount = 1, Reference = "PAGE-PAY-" + id, CreatedBy = 1 });
                database.MeterReadings.Add(new MeterReading { MeterReadingId = id, MeterId = 1, Period = period, ReadingDate = period.AddDays(24), ReadingValue = 30 + id });
            }
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "owner1");
        foreach (var action in new[] { "Charges", "Payments", "Readings" })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/OwnerAccounts/{action}/1?page=999"));
            Assert.Contains("Сторінка 2 з 2", html);
            Assert.DoesNotContain("OWNER-PAY-2", html);
            Assert.DoesNotContain("OWNER-METER-2", html);
            Assert.Contains($"/OwnerAccounts/{action}/1?page=1", html);
        }
        var payments = await client.GetStringAsync("/OwnerAccounts/Payments/1?dateFrom=2026-11-25&dateTo=2026-11-25&page=2");
        Assert.Contains("Знайдено оплат: 21", WebUtility.HtmlDecode(payments));
        Assert.Contains("dateFrom=2026-11-25", payments);
        Assert.Contains("dateTo=2026-11-25", payments);
        var escaped = await client.GetStringAsync("/OwnerAccounts/Payments/1?dateFrom=2026-11-20&dateTo=2026-11-20");
        Assert.Contains("&lt;script&gt;", escaped);
        Assert.DoesNotContain("<script>alert(1)</script>", escaped);
        Assert.Contains("Знайдено нарахувань: 1", WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Charges/1?period=2026-11-01")));
        Assert.Contains("Знайдено показань: 1", WebUtility.HtmlDecode(await client.GetStringAsync("/OwnerAccounts/Readings/1?period=2026-11-01")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingOwnerOrChangingLinkedPayerInvalidatesSession(bool changePayer)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "owner1");
        await factory.ChangeUserAsync("owner1", user =>
        {
            if (changePayer) user.OwnerId = 2;
            else user.IsActive = false;
        });
        foreach (var action in Pages)
        {
            var response = await client.GetAsync($"/OwnerAccounts/{action}/1");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task PayerBoundaryRestrictsReadingsButDoesNotHideLatePayments()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var oldOwner = factory.CreateSeededClient();
        using var nextOwner = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });
        await SeedLedgerAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            (await database.Accounts.FindAsync(1))!.ValidTo = new DateOnly(2026, 12, 1);
            database.Accounts.Add(new Account { AccountId = 3, AccountNumber = "NEW-PAYER", OwnerId = 2, PremiseId = 1, ValidFrom = new DateOnly(2026, 12, 1) });
            database.MeterReadings.Add(new MeterReading { MeterReadingId = 3, MeterId = 1, Period = new DateOnly(2026, 12, 1), ReadingDate = new DateOnly(2026, 12, 25), ReadingValue = 30.125m });
            database.Payments.Add(new Payment { PaymentId = 3, AccountId = 1, PaymentDate = new DateOnly(2027, 1, 15), Amount = 10, Reference = "LATE-OLD-ACCOUNT", CreatedBy = 1 });
            await database.SaveChangesAsync();
        }
        await LoginAsync(oldOwner, "owner1");
        await LoginAsync(nextOwner, "owner2");
        var before = WebUtility.HtmlDecode(await oldOwner.GetStringAsync("/OwnerAccounts/Readings/1"));
        var after = WebUtility.HtmlDecode(await nextOwner.GetStringAsync("/OwnerAccounts/Readings/3"));
        Assert.Matches(">10[.,]125 м³</td>", before);
        Assert.DoesNotMatch(">30[.,]125 м³</td>", before);
        Assert.Matches(">30[.,]125 м³</td>", after);
        Assert.DoesNotMatch(">10[.,]125 м³</td>", after);
        Assert.DoesNotContain("OWNER-METER-2", after);
        Assert.Contains("LATE-OLD-ACCOUNT", await oldOwner.GetStringAsync("/OwnerAccounts/Payments/1?dateFrom=2027-01-15&dateTo=2027-01-15"));
        Assert.DoesNotContain("LATE-OLD-ACCOUNT", await nextOwner.GetStringAsync("/OwnerAccounts/Payments/3"));
        Assert.Equal(HttpStatusCode.NotFound, (await nextOwner.GetAsync("/OwnerAccounts/Details/1")).StatusCode);
    }

    [Theory]
    [InlineData("accountant")]
    [InlineData("chair")]
    public async Task AnonymousAndStaffCannotEnterOwnerPagesBySupplyingOwnerId(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        foreach (var action in Pages)
        {
            var response = await client.GetAsync($"/OwnerAccounts/{action}/1");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
        await LoginAsync(client, login);
        foreach (var action in Pages)
        {
            var response = await client.GetAsync($"/OwnerAccounts/{action}/1?ownerId=1");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task OwnerWithoutAccountsSeesEmptyCabinetAndCannotOpenOtherAccount()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Owners.Add(new Owner { OwnerId = 3, FullName = "Новий власник" });
            await database.SaveChangesAsync();
        }
        await factory.ChangeUserAsync("owner1", user => user.OwnerId = 3);
        await LoginAsync(client, "owner1");
        Assert.Contains("ще не прив’язано", WebUtility.HtmlDecode(await client.GetStringAsync("/Workspace/Owner")));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/OwnerAccounts/Details/1")).StatusCode);
    }
}
