using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class PaymentTests
{
    private static Dictionary<string, string> Fields() => new()
    {
        ["AccountId"] = "1", ["PaymentDate"] = "2026-12-15", ["Amount"] = "100.25", ["Reference"] = "TEST-REF"
    };

    [Theory]
    [InlineData("AccountId", "0")]
    [InlineData("AccountId", "999")]
    [InlineData("PaymentDate", "")]
    [InlineData("PaymentDate", "not-a-date")]
    [InlineData("Amount", "0")]
    [InlineData("Amount", "-1")]
    [InlineData("Amount", "1.001")]
    [InlineData("Amount", "100000000000000000")]
    [InlineData("Reference", "")]
    [InlineData("Reference", "   ")]
    [InlineData("Reference", "long")]
    public async Task InvalidPaymentsAreRejectedBeforeProcedureIsCalled(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        var fields = Fields();
        fields[field] = value == "long" ? new string('X', 101) : value;
        var response = await SubmitAsync(client, "/Payments/Create", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Payments.CountAsync());
    }

    [Theory]
    [InlineData("accountant", true)]
    [InlineData("chair", false)]
    public async Task StaffCanReadAndOnlyAccountantCanRegister(string login, bool canRegister)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, login);
        var html = await client.GetStringAsync("/Payments");
        Assert.Equal(canRegister, html.Contains("href=\"/Payments/Create\""));
        Assert.Equal(canRegister ? HttpStatusCode.OK : HttpStatusCode.Redirect,
            (await client.GetAsync("/Payments/Create")).StatusCode);
        if (!canRegister)
        {
            var fields = Fields();
            fields["__RequestVerificationToken"] = await TokenAsync(client, "/Payments");
            var response = await client.PostAsync("/Payments/Create", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsync("/Payments/Create", new FormUrlEncodedContent(Fields()))).StatusCode);
        }
    }

    [Theory]
    [InlineData("owner1")]
    [InlineData("owner2")]
    public async Task OwnerCannotReadOrRegisterStaffPayments(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var anonymous = await client.GetAsync("/Payments");
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());
        await LoginAsync(client, login);
        foreach (var path in new[] { "/Payments", "/Payments/Create", "/Payments/Details/1" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
        var fields = Fields();
        fields["__RequestVerificationToken"] = await TokenAsync(client, "/Workspace/Owner");
        var post = await client.PostAsync("/Payments/Create", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("AccessDenied", post.Headers.Location!.ToString());
    }

    [Fact]
    public async Task FiltersTotalsPaginationAndDetailsRespectSelectedAccountAndEncodeReference()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Buildings.Add(new Building { BuildingId = 2, Address = "Другий будинок" });
            (await database.Premises.FindAsync(2))!.BuildingId = 2;
            database.Payments.Add(new Payment
            {
                PaymentId = 1, AccountId = 1, PaymentDate = new DateOnly(2026, 11, 30), Amount = 250,
                Reference = "<script>alert(1)</script>", CreatedBy = 1
            });
            for (var id = 2; id <= 22; id++)
                database.Payments.Add(new Payment
                {
                    PaymentId = id, AccountId = 2, PaymentDate = new DateOnly(2026, 12, 1), Amount = 100,
                    Reference = "REF-" + id, CreatedBy = 1
                });
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "chair");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Payments?accountId=2&buildingId=2&dateFrom=2026-12-01&dateTo=2026-12-01&search=REF&page=2"));
        Assert.Contains("Знайдено: 21", html);
        Assert.Matches("2100[.,]00 грн", html);
        Assert.Contains("Сторінка 2 з 2", html);
        Assert.Contains("dateFrom=2026-12-01", html);
        Assert.Contains("accountId=2", html);
        Assert.Contains("Знайдено: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Payments?accountId=1&buildingId=2")));
        Assert.Contains("Знайдено: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Payments?dateFrom=2026-12-02&dateTo=2026-12-01")));
        Assert.Contains("Знайдено: 0", WebUtility.HtmlDecode(await client.GetStringAsync("/Payments?dateFrom=bad")));
        html = await client.GetStringAsync("/Payments/Details/1");
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("accountant", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Payments/Details/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Payments?buildingId=999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Payments?accountId=999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Payments/Edit/1")).StatusCode);
    }
}
