using System.Net;
using System.Text.RegularExpressions;

namespace OsbbBilling.Tests;

internal static class TestForms
{
    public static async Task<string> TokenAsync(HttpClient client, string path = "/Account/Login")
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The form must contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string login, string password = AuthWebApplicationFactory.Password,
        string? returnUrl = null)
    {
        var token = await TokenAsync(client);
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Login"] = login,
            ["Password"] = password,
            ["ReturnUrl"] = returnUrl ?? "",
            ["__RequestVerificationToken"] = token
        }));
    }
}
