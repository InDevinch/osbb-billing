using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;

namespace OsbbBilling.Tests;

internal sealed class AuthWebApplicationFactory(string environment = "Development")
    : WebApplicationFactory<Program>
{
    public const string Password = "TestPassword2026!";
    private readonly string databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<OsbbDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OsbbDbContext>>();
            // Транзакції перевіряються окремо на SQL Server; тут перевіряємо HTTP та бізнес-правила.
            services.AddDbContext<OsbbDbContext>(options => options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }

    public HttpClient CreateSeededClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var users = new[]
        {
            new AppUser { UserId = 1, Login = "accountant", RoleCode = Roles.Accountant },
            new AppUser { UserId = 2, Login = "chair", RoleCode = Roles.Chairperson },
            new AppUser { UserId = 3, Login = "owner1", RoleCode = Roles.Owner, OwnerId = 1 },
            new AppUser { UserId = 4, Login = "owner2", RoleCode = Roles.Owner, OwnerId = 2 },
            new AppUser { UserId = 5, Login = "inactive", RoleCode = Roles.Accountant, IsActive = false }
        };
        foreach (var user in users)
            user.PasswordHash = hasher.HashPassword(user, Password);
        database.Users.AddRange(users);
        database.Buildings.Add(new Building { BuildingId = 1, Address = "Тестовий будинок" });
        database.Owners.AddRange(
            new Owner { OwnerId = 1, FullName = "Тестовий власник 1" },
            new Owner { OwnerId = 2, FullName = "Тестовий власник 2" });
        database.Premises.AddRange(
            new Premise { PremiseId = 1, BuildingId = 1, Number = "1", Kind = "Apartment", Area = 50 },
            new Premise { PremiseId = 2, BuildingId = 1, Number = "2", Kind = "Apartment", Area = 60 });
        database.Accounts.AddRange(
            new Account { AccountId = 1, AccountNumber = "TEST-OWNER-1", OwnerId = 1, PremiseId = 1, ValidFrom = new DateOnly(2026, 1, 1) },
            new Account { AccountId = 2, AccountNumber = "TEST-OWNER-2", OwnerId = 2, PremiseId = 2, ValidFrom = new DateOnly(2026, 1, 1) });
        database.SaveChanges();
        return client;
    }

    public async Task ChangeUserAsync(string login, Action<AppUser> change)
    {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<OsbbDbContext>();
        var user = await database.Users.SingleAsync(x => x.Login == login);
        change(user);
        await database.SaveChangesAsync();
    }
}
