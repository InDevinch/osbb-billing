using System.Net;
using System.Text.RegularExpressions;

namespace OsbbBilling.Tests;

internal static class TestForms
{
    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string path,
        Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(path);
        fields["__RequestVerificationToken"] = TokenFromHtml(html);
        var form = fields.ToList();
        // Числові HTML-поля мають додаткові маркери формату, які браузер надсилає разом із формою.
        foreach (Match match in Regex.Matches(html, "name=\"__Invariant\"[^>]*value=\"([^\"]+)\""))
            form.Add(new("__Invariant", WebUtility.HtmlDecode(match.Groups[1].Value)));
        return await client.PostAsync(path, new FormUrlEncodedContent(form));
    }

    public static async Task<string> TokenAsync(HttpClient client, string path = "/Account/Login")
    {
        var html = await client.GetStringAsync(path);
        return TokenFromHtml(html);
    }

    private static string TokenFromHtml(string html)
    {
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
