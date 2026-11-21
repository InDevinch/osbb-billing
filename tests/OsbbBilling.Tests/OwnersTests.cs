using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class OwnersTests
{
    private static async Task<Owner> ReadOwnerAsync(AuthWebApplicationFactory factory, int id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OsbbDbContext>()
            .Owners.AsNoTracking().SingleAsync(x => x.OwnerId == id);
    }

    [Fact]
    public async Task AccountantCanCreateAndEditContactsWithoutChangingAccountLinks()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        var created = await SubmitAsync(client, "/Owners/Create", new()
        {
            ["FullName"] = "  Новий власник  ", ["Phone"] = "+380501234567", ["Email"] = "new@example.test",
            ["OwnerId"] = "1", ["Accounts[0].AccountId"] = "1", ["User.UserId"] = "1"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/Owners/Details/3", created.Headers.Location!.ToString());
        var owner = await ReadOwnerAsync(factory, 3);
        Assert.Equal("Новий власник", owner.FullName);
        Assert.Equal("+380501234567", owner.Phone);
        Assert.Equal("new@example.test", owner.Email);

        var edited = await SubmitAsync(client, "/Owners/Edit/1", new()
        {
            ["FullName"] = "Змінений власник", ["Phone"] = "", ["Email"] = "",
            ["id"] = "2", ["OwnerId"] = "2", ["Accounts[0].OwnerId"] = "3"
        });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal("/Owners/Details/1", edited.Headers.Location!.ToString());
        var updated = await ReadOwnerAsync(factory, 1);
        Assert.Equal("Змінений власник", updated.FullName);
        Assert.Null(updated.Phone);
        Assert.Null(updated.Email);
        Assert.Equal("Тестовий власник 2", (await ReadOwnerAsync(factory, 2)).FullName);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(1, (await database.Accounts.SingleAsync(x => x.AccountId == 1)).OwnerId);
        Assert.Equal(1, (await database.Users.SingleAsync(x => x.Login == "owner1")).OwnerId);
        Assert.Equal(3, await database.Owners.CountAsync());
        var details = WebUtility.HtmlDecode(await client.GetStringAsync("/Owners/Details/1"));
        Assert.Contains("Змінений власник", details);
        Assert.Contains("TEST-OWNER-1", details);
        Assert.DoesNotContain("TEST-OWNER-2", details);
    }

    [Theory]
    [InlineData("FullName", " ")]
    [InlineData("FullName", "long")]
    [InlineData("Email", "incorrect-email")]
    [InlineData("Phone", "letters")]
    public async Task InvalidFormsDoNotCreateOrUpdateOwner(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        if (value == "long") value = new string('А', 151);
        var fields = new Dictionary<string, string> { ["FullName"] = "Перевірка", ["Phone"] = "", ["Email"] = "" };
        fields[field] = value;
        foreach (var path in new[] { "/Owners/Create", "/Owners/Edit/1" })
        {
            var response = await SubmitAsync(client, path, new(fields));
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("field-validation-error", html);
        }
        Assert.Equal("Тестовий власник 1", (await ReadOwnerAsync(factory, 1)).FullName);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Owners.CountAsync());
    }

    [Fact]
    public async Task ChairpersonCanReadButCannotChangeOwners()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "chair");
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Owners"));
        Assert.Contains("Тестовий власник 1", list);
        Assert.DoesNotContain("Додати власника", list);
        Assert.DoesNotContain("Редагувати", list);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Owners/Details/1")).StatusCode);
        foreach (var path in new[] { "/Owners/Create", "/Owners/Edit/1" })
        {
            var get = await client.GetAsync(path);
            Assert.Contains("/Account/AccessDenied", get.Headers.Location!.ToString());
            var post = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["FullName"] = "Заборонена зміна",
                ["__RequestVerificationToken"] = await TokenAsync(client, "/Owners")
            }));
            Assert.Contains("/Account/AccessDenied", post.Headers.Location!.ToString());
        }
        Assert.Equal("Тестовий власник 1", (await ReadOwnerAsync(factory, 1)).FullName);
    }

    [Theory]
    [InlineData("owner1")]
    [InlineData("")]
    public async Task OwnersAndAnonymousUsersCannotOpenDirectory(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        if (login != "") await LoginAsync(client, login);
        foreach (var path in new[] { "/Owners", "/Owners/Details/2", "/Owners/Create", "/Owners/Edit/2" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied",
                response.Headers.Location!.ToString());
            var post = await client.PostAsync(path, new FormUrlEncodedContent(
                new Dictionary<string, string> { ["FullName"] = "Заборонена зміна" }));
            Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        }
        Assert.Equal("Тестовий власник 2", (await ReadOwnerAsync(factory, 2)).FullName);
    }

    [Fact]
    public async Task WritesRequireAntiforgeryAndMissingOwnersReturn404()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        foreach (var path in new[] { "/Owners/Create", "/Owners/Edit/1" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsync(path, new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["FullName"] = "Без токена" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Owners/Details/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Owners/Edit/999")).StatusCode);
        var missingPost = await client.PostAsync("/Owners/Edit/999", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Немає запису", ["__RequestVerificationToken"] = await TokenAsync(client, "/Owners")
        }));
        Assert.Equal(HttpStatusCode.NotFound, missingPost.StatusCode);
    }

    [Fact]
    public async Task SearchAndPaginationKeepResultsStableAndEncodeNames()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Owners.AddRange(Enumerable.Range(1, 25).Select(i => new Owner
            {
                FullName = $"Пошук {i:D2}", Phone = i == 1 ? "+380501112233" : null,
                Email = i == 1 ? "find@example.test" : null
            }));
            database.Owners.Add(new Owner { FullName = "<script>alert('test')</script>" });
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var first = WebUtility.HtmlDecode(await client.GetStringAsync("/Owners?search=Пошук"));
        Assert.Contains("Пошук 01", first);
        Assert.Contains("Пошук 20", first);
        Assert.DoesNotContain("Пошук 21", first);
        var last = WebUtility.HtmlDecode(await client.GetStringAsync("/Owners?search=Пошук&page=2147483647"));
        Assert.Contains("Пошук 21", last);
        Assert.Contains("Пошук 25", last);
        Assert.DoesNotContain("Пошук 20", last);
        Assert.Contains("Сторінка 2 з 2", last);
        foreach (var search in new[] { "find@example.test", "+380501112233" })
        {
            var result = WebUtility.HtmlDecode(await client.GetStringAsync("/Owners?search=" + Uri.EscapeDataString(search)));
            Assert.Contains("Пошук 01", result);
            Assert.DoesNotContain("Пошук 02", result);
        }
        var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/Owners?search=Відсутній"));
        Assert.Contains("Власників не знайдено.", empty);
        var encoded = await client.GetStringAsync("/Owners?search=script&page=-1");
        Assert.Contains("&lt;script&gt;", encoded);
        Assert.DoesNotContain("<script>", encoded);
    }
}
