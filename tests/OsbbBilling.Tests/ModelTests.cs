using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Tests;

public class ModelTests
{
    private static OsbbDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OsbbDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=True")
            .Options);

    [Fact]
    public void RelationshipsDoNotCreateShadowColumnsOrCascadeDeletes()
    {
        using var database = CreateContext();
        var entities = database.Model.GetEntityTypes().ToList();

        Assert.Equal(13, entities.Count);
        Assert.Equal(18, entities.Sum(x => x.GetForeignKeys().Count()));
        Assert.All(entities.SelectMany(x => x.GetProperties()),
            property => Assert.False(property.IsShadowProperty(), property.Name));
        Assert.All(entities.SelectMany(x => x.GetForeignKeys()),
            key => Assert.Equal(DeleteBehavior.NoAction, key.DeleteBehavior));
        Assert.Equal(new[] { "PaymentId", "ChargeId" },
            database.Model.FindEntityType(typeof(PaymentAllocation))!.FindPrimaryKey()!
                .Properties.Select(x => x.Name));
    }

    [Fact]
    public void ComputedChargeAmountIsNotSentOnInsertOrUpdate()
    {
        using var database = CreateContext();
        var amount = database.Model.FindEntityType(typeof(Charge))!.FindProperty(nameof(Charge.Amount))!;

        Assert.Equal(ValueGenerated.OnAddOrUpdate, amount.ValueGenerated);
        Assert.Equal(PropertySaveBehavior.Ignore, amount.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Ignore, amount.GetAfterSaveBehavior());
        Assert.True(amount.GetIsStored());
        Assert.False(database.Model.FindEntityType(typeof(Charge))!.IsSqlOutputClauseUsed());
    }
}
