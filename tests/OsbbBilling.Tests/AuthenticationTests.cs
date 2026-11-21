using System.Net;
using static OsbbBilling.Tests.TestForms;
using OsbbBilling.Web.Security;

namespace OsbbBilling.Tests;

public class AuthenticationTests
{
    [Fact]
    public async Task AnonymousUsersMustLogInButCanLoadLoginStyles()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        foreach (var path in new[] { "/", "/Workspace/Accountant", "/Workspace/Chairperson", "/Workspace/Owner" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync("/lib/bootstrap/dist/css/bootstrap.min.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
    }

    [Theory]
    [InlineData("accountant", "/Workspace/Accountant", "/Workspace/Chairperson")]
    [InlineData("chair", "/Workspace/Chairperson", "/Workspace/Accountant")]
    [InlineData("owner1", "/Workspace/Owner", "/Workspace/Accountant")]
    public async Task RolesAreEnforcedByServer(string login, string ownPath, string forbiddenPath)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var loginResponse = await LoginAsync(client, login);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        var cookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("OsbbBilling.Auth="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ownPath)).StatusCode);
        var forbidden = await client.GetAsync(forbiddenPath);
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.Contains("/Account/AccessDenied", forbidden.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(forbidden.Headers.Location)).StatusCode);
    }

    [Theory]
    [InlineData("owner1", "TEST-OWNER-1", "TEST-OWNER-2", 2)]
    [InlineData("owner2", "TEST-OWNER-2", "TEST-OWNER-1", 1)]
    public async Task OwnerCannotRequestAnotherPayersAccounts(
        string login, string ownAccount, string otherAccount, int otherOwnerId)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, login);
        var response = await client.GetAsync($"/Workspace/Owner?ownerId={otherOwnerId}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ownAccount, html);
        Assert.DoesNotContain(otherAccount, html);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var diagnostic = await client.GetAsync("/Development/Database");
        Assert.Contains("/Account/AccessDenied", diagnostic.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("accountant", "wrong-password")]
    [InlineData("missing", AuthWebApplicationFactory.Password)]
    [InlineData("inactive", AuthWebApplicationFactory.Password)]
    public async Task InvalidCredentialsAndInactiveUsersDoNotReceiveSession(string login, string password)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var response = await LoginAsync(client, login, password);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Неправильний логін або пароль.", html);
        Assert.DoesNotContain(password, html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Workspace")).StatusCode);
    }

    [Fact]
    public async Task LogoutRequiresPostAndAntiforgeryTokenThenClearsSession()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["Login"] = "accountant", ["Password"] = AuthWebApplicationFactory.Password }))).StatusCode);
        await LoginAsync(client, "accountant");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Account/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(
                new Dictionary<string, string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Workspace/Accountant")).StatusCode);
        var token = await TokenAsync(client, "/Workspace/Accountant");
        var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var protectedPage = await client.GetAsync("/Workspace/Accountant");
        Assert.Contains("/Account/Login", protectedPage.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingUserOrChangingRoleInvalidatesExistingCookie(bool changeRole)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        await factory.ChangeUserAsync("accountant", user =>
        {
            if (changeRole) user.RoleCode = Roles.Chairperson;
            else user.IsActive = false;
        });
        var response = await client.GetAsync("/Workspace/Accountant");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("https://example.test")]
    [InlineData("//example.test")]
    public async Task LoginDoesNotRedirectToExternalReturnUrl(string returnUrl)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var response = await LoginAsync(client, "owner1", returnUrl: returnUrl);
        Assert.Equal("/Workspace", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task LoginReturnsToLocalPageAndDoesNotAcceptRoleFromForm()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        var token = await TokenAsync(client);
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Login"] = "owner1",
            ["Password"] = AuthWebApplicationFactory.Password,
            ["ReturnUrl"] = "/Workspace/Owner",
            ["RoleCode"] = Roles.Accountant,
            ["OwnerId"] = "2",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal("/Workspace/Owner", response.Headers.Location!.ToString());
        var html = await client.GetStringAsync("/Workspace/Owner");
        Assert.Contains("TEST-OWNER-1", html);
        Assert.DoesNotContain("TEST-OWNER-2", html);
        var staffPage = await client.GetAsync("/Workspace/Accountant");
        Assert.Contains("/Account/AccessDenied", staffPage.Headers.Location!.ToString());
    }

    [Fact]
    public async Task LoginAttemptsAreLimited()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, "missing")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "missing")).StatusCode);
    }

    [Fact]
    public async Task ProductionSessionCookieRequiresHttps()
    {
        await using var factory = new AuthWebApplicationFactory("Production");
        using var client = factory.CreateSeededClient();
        var response = await LoginAsync(client, "accountant");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("OsbbBilling.Auth="));
        Assert.Contains("; secure", cookie.ToLowerInvariant());
    }
}
