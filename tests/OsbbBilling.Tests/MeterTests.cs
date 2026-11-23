using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using static OsbbBilling.Tests.TestForms;

namespace OsbbBilling.Tests;

public class MeterTests
{
    private static async Task SeedMetersAsync(AuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        database.Services.AddRange(
            new Service { ServiceId = 1, Name = "Утримання", Unit = "м²", CalculationMethod = "Area" },
            new Service { ServiceId = 2, Name = "Вода", Unit = "м³", CalculationMethod = "Meter" });
        database.PremiseServices.AddRange(
            new PremiseService { PremiseServiceId = 1, PremiseId = 1, ServiceId = 1, ValidFrom = new DateOnly(2026, 1, 1) },
            new PremiseService { PremiseServiceId = 2, PremiseId = 1, ServiceId = 2, ValidFrom = new DateOnly(2026, 1, 1) },
            new PremiseService { PremiseServiceId = 3, PremiseId = 2, ServiceId = 2, ValidFrom = new DateOnly(2026, 1, 1) });
        database.Meters.Add(new Meter { MeterId = 1, PremiseServiceId = 2, SerialNumber = "TEST-METER", InstalledOn = new DateOnly(2026, 1, 1), InitialReading = 100 });
        database.MeterReadings.AddRange(
            new MeterReading { MeterReadingId = 1, MeterId = 1, Period = new DateOnly(2026, 1, 1), ReadingDate = new DateOnly(2026, 1, 31), ReadingValue = 105 },
            new MeterReading { MeterReadingId = 2, MeterId = 1, Period = new DateOnly(2026, 2, 1), ReadingDate = new DateOnly(2026, 2, 28), ReadingValue = 110 });
        await database.SaveChangesAsync();
    }

    private static Dictionary<string, string> InstallFields() => new()
    {
        ["PremiseServiceId"] = "3", ["SerialNumber"] = "NEW-METER", ["InstalledOn"] = "2026-01-01",
        ["RemovedOn"] = "", ["InitialReading"] = "10.125"
    };
    private static Dictionary<string, string> ReadingFields() => new()
    {
        ["MeterId"] = "1", ["Period"] = "2026-03-01", ["ReadingDate"] = "2026-03-31", ["ReadingValue"] = "115.125"
    };

    [Fact]
    public async Task MeterCanBeReplacedAtMonthBoundaryAndFirstReadingUsesNewInitialValue()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        await LoginAsync(client, "accountant");
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/Meters/Remove/1", new() { ["RemovedOn"] = "2026-03-01", ["InitialReading"] = "0" })).StatusCode);
        var outside = await SubmitAsync(client, "/MeterReadings/Create?meterId=1", ReadingFields());
        Assert.Contains("період роботи", WebUtility.HtmlDecode(await outside.Content.ReadAsStringAsync()));
        var fields = InstallFields();
        fields["PremiseServiceId"] = "2";
        fields["InstalledOn"] = "2026-03-01";
        fields["InitialReading"] = "0";
        fields["MeterId"] = "1";
        var installed = await SubmitAsync(client, "/Meters/Create", fields);
        Assert.Equal("/Meters/Details/2", installed.Headers.Location!.ToString());
        var reading = ReadingFields();
        reading["MeterId"] = "2";
        reading["ReadingValue"] = "0.875";
        Assert.Equal(HttpStatusCode.Redirect,
            (await SubmitAsync(client, "/MeterReadings/Create?meterId=2", reading)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        Assert.Equal(100m, (await database.Meters.FindAsync(1))!.InitialReading);
        Assert.Equal(new DateOnly(2026, 3, 1), (await database.Meters.FindAsync(1))!.RemovedOn);
        Assert.Equal(0m, (await database.Meters.FindAsync(2))!.InitialReading);
        var html = await client.GetStringAsync("/Meters/Details/2");
        Assert.Matches(">0[.,]875</td>", html);
    }

    [Theory]
    [InlineData("PremiseServiceId", "1")]
    [InlineData("PremiseServiceId", "999")]
    [InlineData("SerialNumber", "")]
    [InlineData("SerialNumber", "TEST-METER")]
    [InlineData("InstalledOn", "2026-01-02")]
    [InlineData("InstalledOn", "2025-12-01")]
    [InlineData("RemovedOn", "2026-01-01")]
    [InlineData("InitialReading", "-1")]
    [InlineData("InitialReading", "1.2345")]
    [InlineData("InitialReading", "1000000000000000")]
    public async Task InvalidInstallationsDoNotPersist(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = InstallFields();
        fields[field] = value;
        var response = await SubmitAsync(client, "/Meters/Create", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().Meters.CountAsync());
    }

    [Fact]
    public async Task OverlapsAndServiceEndAreChecked()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = InstallFields();
        fields["PremiseServiceId"] = "2";
        var overlap = await SubmitAsync(client, "/Meters/Create", fields);
        Assert.Contains("інший лічильник", WebUtility.HtmlDecode(await overlap.Content.ReadAsStringAsync()));
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            (await database.PremiseServices.FindAsync(3))!.ValidTo = new DateOnly(2026, 5, 1);
            await database.SaveChangesAsync();
        }
        fields["PremiseServiceId"] = "3";
        var unbounded = await SubmitAsync(client, "/Meters/Create", fields);
        Assert.Contains("період підключення", WebUtility.HtmlDecode(await unbounded.Content.ReadAsStringAsync()));
        fields["RemovedOn"] = "2026-05-01";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/Meters/Create", fields)).StatusCode);
    }

    [Theory]
    [InlineData("Period", "2026-03-02")]
    [InlineData("Period", "2025-12-01")]
    [InlineData("Period", "2026-05-01")]
    [InlineData("ReadingDate", "2026-04-01")]
    [InlineData("ReadingValue", "99")]
    [InlineData("ReadingValue", "109")]
    [InlineData("ReadingValue", "-1")]
    [InlineData("ReadingValue", "110.1234")]
    [InlineData("ReadingValue", "1000000000000000")]
    public async Task InvalidReadingsDoNotPersist(string field, string value)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = ReadingFields();
        fields[field] = value;
        var response = await SubmitAsync(client, "/MeterReadings/Create?meterId=1", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().MeterReadings.CountAsync());
    }

    [Fact]
    public async Task UnbilledReadingCanChangeButCannotMoveToAnotherMeterOrExceedNextValue()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        await LoginAsync(client, "accountant");
        var fields = ReadingFields();
        fields["MeterId"] = "999";
        fields["Period"] = "2026-06-01";
        fields["ReadingDate"] = "2026-01-30";
        fields["ReadingValue"] = "106";
        fields["id"] = "2";
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitAsync(client, "/MeterReadings/Edit/1", fields)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var reading = await scope.ServiceProvider.GetRequiredService<OsbbDbContext>().MeterReadings.FindAsync(1);
            Assert.Equal(1, reading!.MeterId);
            Assert.Equal(new DateOnly(2026, 1, 1), reading.Period);
            Assert.Equal(106m, reading.ReadingValue);
        }
        fields["ReadingValue"] = "111";
        var tooHigh = await SubmitAsync(client, "/MeterReadings/Edit/1", fields);
        Assert.Contains("перевищувати наступне", WebUtility.HtmlDecode(await tooHigh.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task UsedReadingsDuplicateMonthsAndRemovalCutoffAreBlocked()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
            database.Charges.Add(new Charge { ChargeId = 1, AccountId = 1, PremiseServiceId = 2, Period = new DateOnly(2026, 2, 1) });
            await database.SaveChangesAsync();
        }
        await LoginAsync(client, "accountant");
        var edit = ReadingFields();
        edit["Period"] = "2026-01-01";
        edit["ReadingDate"] = "2026-01-30";
        edit["ReadingValue"] = "106";
        var used = await SubmitAsync(client, "/MeterReadings/Edit/1", edit);
        Assert.Contains("вже використане", WebUtility.HtmlDecode(await used.Content.ReadAsStringAsync()));
        var duplicate = ReadingFields();
        duplicate["Period"] = "2026-02-01";
        duplicate["ReadingDate"] = "2026-02-28";
        var response = await SubmitAsync(client, "/MeterReadings/Create?meterId=1", duplicate);
        Assert.Contains("вже внесено", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        var skipped = ReadingFields();
        skipped["Period"] = "2026-05-01";
        skipped["ReadingDate"] = "2026-05-31";
        var gap = await SubmitAsync(client, "/MeterReadings/Create?meterId=1", skipped);
        Assert.Contains("за попередній місяць", WebUtility.HtmlDecode(await gap.Content.ReadAsStringAsync()));
        var removed = await SubmitAsync(client, "/Meters/Remove/1", new() { ["RemovedOn"] = "2026-02-01" });
        Assert.Contains("виключає вже внесені", WebUtility.HtmlDecode(await removed.Content.ReadAsStringAsync()));
        var details = WebUtility.HtmlDecode(await client.GetStringAsync("/Meters/Details/1"));
        Assert.Contains("Використано в нарахуванні", details);
    }

    [Theory]
    [InlineData("chair")]
    [InlineData("owner1")]
    [InlineData("")]
    public async Task RoleRestrictionsProtectMetersAndReadings(string login)
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        if (login != "") await LoginAsync(client, login);
        foreach (var path in new[] { "/Meters", "/Meters/Details/1" })
        {
            var response = await client.GetAsync(path);
            if (login == "chair") Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            else Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/Meters/Create", "/Meters/Remove/1", "/MeterReadings/Create?meterId=1", "/MeterReadings/Edit/1" })
        {
            var get = await client.GetAsync(path);
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", get.Headers.Location!.ToString());
            var post = await client.PostAsync(path, new FormUrlEncodedContent(ReadingFields()));
            Assert.Contains(login == "" ? "/Account/Login" : "/Account/AccessDenied", post.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task WritesRequireTokensAndUnknownRecordsReturn404()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateSeededClient();
        await SeedMetersAsync(factory);
        await LoginAsync(client, "accountant");
        foreach (var path in new[] { "/Meters/Create", "/Meters/Remove/1", "/MeterReadings/Create", "/MeterReadings/Edit/1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent(ReadingFields()))).StatusCode);
        foreach (var path in new[] { "/Meters/Details/999", "/Meters/Remove/999", "/MeterReadings/Edit/999", "/MeterReadings/Create?meterId=999" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }
}
