using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OSBB_TEST_CONNECTION_STRING")))
            Skip = "Set OSBB_TEST_CONNECTION_STRING to run read-only SQL Server checks.";
    }
}

[Trait("Category", "Integration")]
public class DatabaseReadTests
{
    private static OsbbDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OsbbDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("OSBB_TEST_CONNECTION_STRING"))
            .Options);

    [SqlServerFact]
    public async Task ModelMatchesExistingColumnsAndTriggerTables()
    {
        await using var database = CreateContext();
        await using var connection = new SqlConnection(
            Environment.GetEnvironmentVariable("OSBB_TEST_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.name, c.name, ty.name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, c.is_computed,
                   CAST(CASE WHEN EXISTS (
                       SELECT 1 FROM sys.triggers tr WHERE tr.parent_id = t.object_id
                   ) THEN 1 ELSE 0 END AS bit)
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = 'dbo' AND t.is_ms_shipped = 0;
            """;
        var columns = new Dictionary<(string Table, string Column), ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sqlType = reader.GetString(2);
            var length = reader.GetInt16(3);
            var precision = reader.GetByte(4);
            var scale = reader.GetByte(5);
            var storeType = sqlType switch
            {
                "nvarchar" => $"nvarchar({length / 2})",
                "varchar" => $"varchar({length})",
                "decimal" => $"decimal({precision},{scale})",
                "datetime2" => $"datetime2({scale})",
                _ => sqlType
            };
            columns.Add((reader.GetString(0), reader.GetString(1)),
                new(storeType, reader.GetBoolean(6), reader.GetBoolean(7),
                    reader.GetBoolean(8), reader.GetBoolean(9)));
        }

        var entities = database.Model.GetEntityTypes().ToList();
        Assert.Equal(columns.Keys.Select(x => x.Table).Distinct().Order(),
            entities.Select(x => x.GetTableName()!).Order());
        Assert.Equal(columns.Count, entities.Sum(x => x.GetProperties().Count()));

        foreach (var entity in entities)
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (var property in entity.GetProperties())
            {
                var key = (table.Name, property.GetColumnName(table)!);
                Assert.True(columns.TryGetValue(key, out var column), $"Missing column {key}");
                Assert.Equal(column!.StoreType, property.GetColumnType()!.Replace(" ", ""));
                Assert.Equal(column.IsIdentity,
                    property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn);
                Assert.Equal(column.IsComputed, property.GetComputedColumnSql() is not null);
                // SQL Server reports this computed expression as nullable, although both inputs are required.
                if (!column.IsComputed)
                    Assert.Equal(column.IsNullable, property.IsNullable);
                if (column.HasTrigger)
                    Assert.False(entity.IsSqlOutputClauseUsed(), table.Name);
            }
        }
    }

    [SqlServerFact]
    public async Task AllEntitiesAndRelationshipsCanBeRead()
    {
        await using var database = CreateContext();
        await ReadSample<Building>(database);
        await ReadSample<Owner>(database);
        await ReadSample<Premise>(database);
        await ReadSample<Account>(database);
        await ReadSample<AppUser>(database);
        await ReadSample<Service>(database);
        await ReadSample<Tariff>(database);
        await ReadSample<PremiseService>(database);
        await ReadSample<Meter>(database);
        await ReadSample<MeterReading>(database);
        await ReadSample<Charge>(database);
        await ReadSample<Payment>(database);
        await ReadSample<PaymentAllocation>(database);

        var accounts = await database.Accounts.AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.Premise).ThenInclude(x => x.Building)
            .Take(5).ToListAsync();
        Assert.All(accounts, account =>
        {
            Assert.NotNull(account.Owner);
            Assert.NotNull(account.Premise.Building);
        });

        var charges = await database.Charges.AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.PremiseService).ThenInclude(x => x.Service)
            .Include(x => x.Tariff)
            .Include(x => x.CreatedByUser)
            .Include(x => x.PaymentAllocations).ThenInclude(x => x.Payment)
            .Take(5).ToListAsync();
        Assert.All(charges, charge =>
        {
            Assert.Equal(decimal.Round(charge.Quantity * charge.UnitPrice, 2,
                MidpointRounding.AwayFromZero), charge.Amount);
            Assert.Equal(charge.AccountId, charge.Account.AccountId);
            Assert.Equal(charge.PremiseService.ServiceId, charge.Tariff.ServiceId);
            Assert.Equal(charge.CreatedBy, charge.CreatedByUser.UserId);
            Assert.All(charge.PaymentAllocations,
                allocation => Assert.Equal(charge.AccountId, allocation.Payment.AccountId));
        });
        Assert.Empty(database.ChangeTracker.Entries());
    }

    private static async Task ReadSample<TEntity>(OsbbDbContext database) where TEntity : class =>
        await database.Set<TEntity>().AsNoTracking().Take(5).ToListAsync();

    private sealed record ColumnInfo(
        string StoreType, bool IsNullable, bool IsIdentity, bool IsComputed, bool HasTrigger);
}
