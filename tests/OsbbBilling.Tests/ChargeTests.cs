using System.Net;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class ChargeTests
{
    [Theory]
    [InlineData("accountant", true)]
    [InlineData("chair", false)]
    public async Task StaffCanReadButOnlyAccountantCanGenerate(string login, bool canGenerate)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, login);
        var html = await client.GetStringAsync("/Charges");
        Assert.Equal(canGenerate, html.Contains("href=\"/Charges/Generate\""));
        var form = await client.GetAsync("/Charges/Generate");
        Assert.Equal(canGenerate ? HttpStatusCode.OK : HttpStatusCode.Redirect, form.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Charges/Details/999")).StatusCode);
        if (!canGenerate)
        {
            var token = await TokenAsync(client, "/Charges");
            var response = await client.PostAsync("/Charges/Generate", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Period"] = "2026-12-01", ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-12-02")]
    [InlineData("wrong-date")]
    public async Task InvalidMonthIsRejectedBeforeCallingDatabase(string period)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        var response = await SubmitAsync(client, "/Charges/Generate", new() { ["Period"] = period });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("owner1")]
    [InlineData("owner2")]
    public async Task OwnerCannotOpenStaffCharges(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, login);
        foreach (var path in new[] { "/Charges", "/Charges/Generate", "/Charges/Details/1" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task AnonymousIsSentToLoginAndGenerationRequiresAntiforgery()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var response = await client.GetAsync("/Charges");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        await LoginAsync(client, "accountant");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Charges/Generate",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Period"] = "2026-12-01" }))).StatusCode);
    }
}
