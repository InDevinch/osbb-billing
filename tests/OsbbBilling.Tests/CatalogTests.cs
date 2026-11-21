using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class CatalogTests
{
    private static async Task SeedCatalogAsync(AuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        database.Services.AddRange(
            new Service { ServiceId = 1, Name = "Утримання", Unit = "м²", CalculationMethod = "Area" },
            new Service { ServiceId = 2, Name = "Вода", Unit = "м³", CalculationMethod = "Meter" });
        database.Tariffs.Add(new Tariff { TariffId = 1, BuildingId = 1, ServiceId = 1, UnitPrice = 8, ValidFrom = new DateOnly(2026, 1, 1) });
        database.PremiseServices.AddRange(
            new PremiseService { PremiseServiceId = 1, PremiseId = 1, ServiceId = 1, ValidFrom = new DateOnly(2026, 1, 1) },
            new PremiseService { PremiseServiceId = 2, PremiseId = 1, ServiceId = 2, ValidFrom = new DateOnly(2026, 1, 1) });
        database.Charges.Add(new Charge { ChargeId = 1, AccountId = 1, TariffId = 1, PremiseServiceId = 1, Period = new DateOnly(2026, 2, 1) });
        database.Meters.Add(new Meter { MeterId = 1, PremiseServiceId = 2, SerialNumber = "TEST-METER", InstalledOn = new DateOnly(2026, 1, 1), InitialReading = 100 });
        await database.SaveChangesAsync();
    }

    private static Dictionary<string, string> TariffFields() => new()
    {
        ["BuildingId"] = "1", ["ServiceId"] = "2", ["UnitPrice"] = "40.1234",
        ["ValidFrom"] = "2026-03-01", ["ValidTo"] = ""
    };

    [Theory]
    [InlineData("Area")]
    [InlineData("Residents")]
    [InlineData("Meter")]
    [InlineData("Fixed")]
    public async Task AccountantCanCreateAndEditUnusedService(string method)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = new Dictionary<string, string> { ["Name"] = "  Нова послуга  ", ["Unit"] = "од.", ["CalculationMethod"] = method };
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Services/Create", fields)).StatusCode);
        fields["Name"] = "Перейменована";
        fields["Unit"] = "місяць";
        fields["CalculationMethod"] = "Fixed";
        fields["id"] = "1";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Services/Edit/3", fields)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var service = await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Services.FindAsync(3);
        Assert.Equal("Перейменована", service!.Name);
        Assert.Equal("Fixed", service.CalculationMethod);
        Assert.Equal("місяць", service.Unit);
    }

    [Theory]
    [InlineData("Name", "")]
    [InlineData("Unit", "")]
    [InlineData("Unit", "123456789012345678901")]
    [InlineData("CalculationMethod", "Other")]
    [InlineData("Name", "Утримання")]
    public async Task InvalidOrDuplicateServicesAreRejected(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = new Dictionary<string, string> { ["Name"] = "Нова", ["Unit"] = "од.", ["CalculationMethod"] = "Fixed" };
        fields[field] = value;
        var response = await SubmitAsync(client, "/Services/Create", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Services.CountAsync());
    }

    [Fact]
    public async Task UsedServiceAllowsNameChangeButLocksUnitAndMethod()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = new Dictionary<string, string> { ["Name"] = "Нова назва", ["Unit"] = "м³", ["CalculationMethod"] = "Meter", ["HasAssignments"] = "false" };
        var blocked = await SubmitAsync(client, "/Services/Edit/1", fields);
        Assert.Contains("змінювати не можна", WebUtility.HtmlDecode(await blocked.Content.ReadAsStringAsync()));
        fields["Unit"] = "м²";
        fields["CalculationMethod"] = "Area";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Services/Edit/1", fields)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var service = await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Services.FindAsync(1);
        Assert.Equal("Нова назва", service!.Name);
        Assert.Equal("Area", service.CalculationMethod);
    }

    [Theory]
    [InlineData("UnitPrice", "-1")]
    [InlineData("UnitPrice", "1.12345")]
    [InlineData("UnitPrice", "100000000000000")]
    [InlineData("BuildingId", "999")]
    [InlineData("ServiceId", "999")]
    [InlineData("ValidFrom", "2026-03-02")]
    [InlineData("ValidFrom", "")]
    [InlineData("ValidTo", "2026-02-01")]
    [InlineData("ValidTo", "2026-04-02")]
    public async Task InvalidTariffsDoNotPersist(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = TariffFields();
        fields[field] = value;
        var response = await SubmitAsync(client, "/Tariffs/Create", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Tariffs.CountAsync());
    }

    [Fact]
    public async Task TariffVersionsPreserveOldPricesAndIgnoreForgedForeignKeys()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = TariffFields();
        fields["BuildingId"] = "999";
        fields["ServiceId"] = "2";
        fields["UnitPrice"] = "9.4321";
        fields["id"] = "999";
        var response = await SubmitAsync(client, "/Tariffs/NewVersion/1", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            var old = await database.Tariffs.FindAsync(1);
            Assert.Equal(8m, old!.UnitPrice);
            Assert.Equal(new DateOnly(2026, 3, 1), old.ValidTo);
            var next = await database.Tariffs.FindAsync(2);
            Assert.Equal(9.4321m, next!.UnitPrice);
            Assert.Equal(1, next.ServiceId);
            Assert.Equal(1, next.BuildingId);
            Assert.Equal(1, (await database.Charges.FindAsync(1))!.TariffId);
        }
        var repeated = await SubmitAsync(client, "/Tariffs/NewVersion/1", fields);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Contains("вже завершено", WebUtility.HtmlDecode(await repeated.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task OverlapsAndChargesCutoffAreRejectedButAdjacentZeroTariffIsAllowed()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = TariffFields();
        fields["ServiceId"] = "1";
        var overlap = await SubmitAsync(client, "/Tariffs/Create", fields);
        Assert.Contains("перетинається", WebUtility.HtmlDecode(await overlap.Content.ReadAsStringAsync()));
        fields["ValidFrom"] = "2026-02-01";
        var cutoff = await SubmitAsync(client, "/Tariffs/NewVersion/1", fields);
        Assert.Contains("уже є нарахування", WebUtility.HtmlDecode(await cutoff.Content.ReadAsStringAsync()));
        fields = TariffFields();
        fields["UnitPrice"] = "0";
        fields["ValidTo"] = "2026-05-01";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Tariffs/Create", fields)).StatusCode);
        fields["ValidFrom"] = "2026-05-01";
        fields["ValidTo"] = "";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Tariffs/Create", fields)).StatusCode);
    }

    [Fact]
    public async Task AssignmentsStopSafelyAndMetersMustBeRemovedFirst()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        var tooEarly = await SubmitAsync(client, "/PremiseServices/Stop/1", new() { ["ValidTo"] = "2026-02-01" });
        Assert.Contains("виключає наявні", WebUtility.HtmlDecode(await tooEarly.Content.ReadAsStringAsync()));
        var meter = await SubmitAsync(client, "/PremiseServices/Stop/2", new() { ["ValidTo"] = "2026-03-01" });
        Assert.Contains("роботу лічильника", WebUtility.HtmlDecode(await meter.Content.ReadAsStringAsync()));
        var stop = await SubmitAsync(client, "/PremiseServices/Stop/1", new() { ["ValidTo"] = "2026-03-01", ["ServiceId"] = "2" });
        Assert.Equal(HttpStatusCode.Redirect, stop.StatusCode);
        var fields = new Dictionary<string, string> { ["PremiseId"] = "1", ["ServiceId"] = "1", ["ValidFrom"] = "2026-03-01", ["ValidTo"] = "" };
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/PremiseServices/Create", fields)).StatusCode);
        var overlap = await SubmitAsync(client, "/PremiseServices/Create", fields);
        Assert.Contains("перетинається", WebUtility.HtmlDecode(await overlap.Content.ReadAsStringAsync()));
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(new DateOnly(2026, 3, 1), (await database.PremiseServices.FindAsync(1))!.ValidTo);
        Assert.Null((await database.PremiseServices.FindAsync(2))!.ValidTo);
        Assert.Equal(1, (await database.Charges.FindAsync(1))!.PremiseServiceId);
    }

    [Theory]
    [InlineData("chair")]
    [InlineData("owner1")]
    [InlineData("")]
    public async Task CatalogRoleRestrictionsAndAntiforgeryAreApplied(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        if (login != "") await LoginAsync(client, login);
        foreach (var path in new[] { "/Services", "/Tariffs", "/PremiseServices" })
        {
            var response = await client.GetAsync(path);
            if (login == "chair") Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            else Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/Services/Create", "/Services/Edit/1", "/Tariffs/Create", "/Tariffs/NewVersion/1", "/PremiseServices/Create", "/PremiseServices/Stop/1" })
        {
            var response = await client.PostAsync(path, new FormUrlEncodedContent(TariffFields()));
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", response.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task MissingIdsAndForgedPostWithoutTokenAreRejected()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedCatalogAsync(factory);
        await LoginAsync(client, "accountant");
        foreach (var path in new[] { "/Services/Edit/999", "/Tariffs/NewVersion/999", "/PremiseServices/Stop/999", "/Tariffs?buildingId=999", "/PremiseServices?serviceId=999" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/Services/Create", "/Services/Edit/1", "/Tariffs/Create", "/Tariffs/NewVersion/1", "/PremiseServices/Create", "/PremiseServices/Stop/1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent(TariffFields()))).StatusCode);
    }
}
