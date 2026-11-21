using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class BuildingsAndPremisesTests
{
    private static Dictionary<string, string> PremiseFields(int buildingId = 1, string number = "NEW") => new()
    {
        ["BuildingId"] = buildingId.ToString(), ["Number"] = number,
        ["Kind"] = "Apartment", ["Area"] = "72.35", ["ResidentsCount"] = "2"
    };

    [Fact]
    public async Task AccountantCanCreateEditAndMoveUnusedPremiseWithoutChangingOtherRecords()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        var building = await SubmitAsync(client, "/Buildings/Create",
            new() { ["Address"] = "  Новий будинок  ", ["BuildingId"] = "1" });
        Assert.Equal(HttpStatusCode.Redirect, building.StatusCode);
        var buildingEdit = await SubmitAsync(client, "/Buildings/Edit/2",
            new() { ["Address"] = "Оновлена адреса", ["id"] = "1", ["Premises[0].PremiseId"] = "1" });
        Assert.Equal(HttpStatusCode.Redirect, buildingEdit.StatusCode);
        var fields = PremiseFields(2, "  A-1  ");
        fields["Kind"] = "Nonres";
        fields["ResidentsCount"] = "0";
        fields["PremiseId"] = "1";
        fields["Accounts[0].AccountId"] = "1";
        var created = await SubmitAsync(client, "/Premises/Create", fields);
        Assert.True(created.StatusCode == HttpStatusCode.Redirect,
            WebUtility.HtmlDecode(await created.Content.ReadAsStringAsync()));
        Assert.Equal("/Premises/Details/3", created.Headers.Location!.ToString());
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            var premise = await database.Premises.SingleAsync(x => x.PremiseId == 3);
            Assert.Equal("A-1", premise.Number);
            Assert.Equal("Nonres", premise.Kind);
            Assert.Equal(72.35m, premise.Area);
            Assert.Equal((short)0, premise.ResidentsCount);
        }
        var editFields = PremiseFields(1, "99");
        editFields["Area"] = "55.50";
        editFields["ResidentsCount"] = "3";
        editFields["id"] = "1";
        var edited = await SubmitAsync(client, "/Premises/Edit/3", editFields);
        Assert.Equal("/Premises/Details/3", edited.Headers.Location!.ToString());
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            Assert.Equal("Тестовий будинок", (await database.Buildings.FindAsync(1))!.Address);
            Assert.Equal("Оновлена адреса", (await database.Buildings.FindAsync(2))!.Address);
            var premise = await database.Premises.FindAsync(3);
            Assert.Equal(1, premise!.BuildingId);
            Assert.Equal(55.50m, premise.Area);
            Assert.Equal("Apartment", premise.Kind);
            Assert.Equal((short)3, premise.ResidentsCount);
            Assert.Equal("1", (await database.Premises.FindAsync(1))!.Number);
            Assert.Equal(1, (await database.Accounts.FindAsync(1))!.PremiseId);
        }
        var details = WebUtility.HtmlDecode(await client.GetStringAsync("/Premises/Details/1"));
        Assert.Contains("TEST-OWNER-1", details);
        Assert.Contains("Тестовий власник 1", details);
    }

    [Theory]
    [InlineData("Number", " ")]
    [InlineData("Number", "123456789012345678901")]
    [InlineData("Kind", "Unknown")]
    [InlineData("Area", "0")]
    [InlineData("Area", "-1")]
    [InlineData("Area", "1.234")]
    [InlineData("Area", "100000000")]
    [InlineData("ResidentsCount", "-1")]
    [InlineData("ResidentsCount", "32768")]
    [InlineData("ResidentsCount", "0.5")]
    [InlineData("BuildingId", "999")]
    public async Task InvalidPremiseFieldsNeverPersist(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        foreach (var path in new[] { "/Premises/Create", "/Premises/Edit/1" })
        {
            var fields = PremiseFields();
            fields[field] = value;
            var response = await SubmitAsync(client, path, fields);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("field-validation-error", html);
        }
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(2, await database.Premises.CountAsync());
        Assert.Equal(50m, (await database.Premises.FindAsync(1))!.Area);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("long")]
    public async Task BuildingAddressIsRequiredAndLimited(string address)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        if (address == "long") address = new string('А', 251);
        foreach (var path in new[] { "/Buildings/Create", "/Buildings/Edit/1" })
        {
            var response = await SubmitAsync(client, path, new() { ["Address"] = address });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        }
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(1, await database.Buildings.CountAsync());
        Assert.Equal("Тестовий будинок", (await database.Buildings.FindAsync(1))!.Address);
    }

    [Fact]
    public async Task DuplicatesAreRejectedButSameNumberInAnotherBuildingIsAllowed()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        await SubmitAsync(client, "/Buildings/Create", new() { ["Address"] = "Другий будинок" });
        foreach (var path in new[] { "/Buildings/Create", "/Buildings/Edit/2" })
        {
            var response = await SubmitAsync(client, path, new() { ["Address"] = " Тестовий будинок " });
            Assert.Contains("такою адресою вже є", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        var duplicate = await SubmitAsync(client, "/Premises/Create", PremiseFields(1, " 1 "));
        Assert.Contains("таким номером", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));
        var allowed = await SubmitAsync(client, "/Premises/Create", PremiseFields(2, "1"));
        Assert.Equal(HttpStatusCode.Redirect, allowed.StatusCode);
        var unchanged = await SubmitAsync(client, "/Premises/Edit/3", PremiseFields(2, "1"));
        Assert.Equal(HttpStatusCode.Redirect, unchanged.StatusCode);
        var editDuplicate = await SubmitAsync(client, "/Premises/Edit/2", PremiseFields(1, "1"));
        Assert.Contains("таким номером", WebUtility.HtmlDecode(await editDuplicate.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountsOrServicesPreventMovingPremise(bool serviceOnly)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Buildings.Add(new Building { BuildingId = 2, Address = "Інший будинок" });
            if (serviceOnly)
            {
                database.Premises.Add(new Premise { PremiseId = 3, BuildingId = 1, Number = "3", Kind = "Apartment", Area = 40 });
                database.Services.Add(new Service { ServiceId = 1, Name = "Тестова послуга", Unit = "м²", CalculationMethod = "Area" });
                database.PremiseServices.Add(new PremiseService
                {
                    PremiseServiceId = 1, PremiseId = 3, ServiceId = 1, ValidFrom = new DateOnly(2026, 1, 1)
                });
            }
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var id = serviceOnly ? 3 : 1;
        var fields = PremiseFields(2, serviceOnly ? "3" : "1");
        fields["HasHistory"] = "false";
        var response = await SubmitAsync(client, $"/Premises/Edit/{id}", fields);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("не можна переносити", html);
        using var checkScope = factory.Services.CreateScope();
        var premise = await checkScope.ServiceProvider.GetRequiredService<OsbbDbContext>().Premises.FindAsync(id);
        Assert.Equal(1, premise!.BuildingId);
        Assert.Equal(serviceOnly ? 40m : 50m, premise.Area);
    }

    [Theory]
    [InlineData("chair")]
    [InlineData("owner1")]
    [InlineData("")]
    public async Task RoleRestrictionsApplyToEveryDirectoryAction(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        if (login != "") await LoginAsync(client, login);
        foreach (var path in new[] { "/Buildings", "/Premises", "/Premises/Details/1" })
        {
            var response = await client.GetAsync(path);
            if (login == "chair")
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain("Редагувати", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
            }
            else
                Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/Buildings/Create", "/Buildings/Edit/1", "/Premises/Create", "/Premises/Edit/1" })
        {
            var get = await client.GetAsync(path);
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", get.Headers.Location!.ToString());
            var post = await client.PostAsync(path, new FormUrlEncodedContent(PremiseFields()));
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", post.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task WritesNeedAntiforgeryAndMissingRecordsReturn404()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await LoginAsync(client, "accountant");
        foreach (var path in new[] { "/Buildings/Create", "/Buildings/Edit/1", "/Premises/Create", "/Premises/Edit/1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path,
                new FormUrlEncodedContent(PremiseFields()))).StatusCode);
        foreach (var path in new[] { "/Buildings/Edit/999", "/Premises/Edit/999", "/Premises/Details/999",
                     "/Premises?buildingId=999", "/Premises/Create?buildingId=999" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        var token = await TokenAsync(client, "/Buildings/Create");
        var missing = await client.PostAsync("/Buildings/Edit/999", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Address"] = "Немає запису", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task BuildingFilterAndPaginationKeepSearchAndEncodeUserText()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Buildings.Add(new Building { BuildingId = 2, Address = "Будинок фільтра <script>" });
            database.Premises.AddRange(Enumerable.Range(1, 25).Select(i => new Premise
            {
                BuildingId = 2, Number = $"P{i:D2}", Kind = "Nonres", Area = 25, ResidentsCount = 0
            }));
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var buildings = await client.GetStringAsync("/Buildings?search=script&page=-1");
        Assert.Contains("&lt;script&gt;", buildings);
        Assert.DoesNotContain("<script>", buildings);
        var first = await client.GetStringAsync("/Premises?buildingId=2&search=P");
        Assert.Contains(">P01</a>", first);
        Assert.Contains(">P20</a>", first);
        Assert.DoesNotContain(">P21</a>", first);
        Assert.Contains("buildingId=2", first);
        Assert.Contains("search=P", first);
        var last = await client.GetStringAsync("/Premises?buildingId=2&search=P&page=2147483647");
        Assert.Contains(">P21</a>", last);
        Assert.Contains(">P25</a>", last);
        Assert.DoesNotContain(">P20</a>", last);
        var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/Premises?buildingId=1&search=P"));
        Assert.Contains("Приміщень не знайдено.", empty);
    }
}
