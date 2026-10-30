using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Configurations;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ApexPost.Infrastructure.Tests.Persistence;

/// <summary>
/// Checks the relational model that the migrations are generated from. No database is opened:
/// building the model only needs the SQL Server provider.
/// </summary>
public sealed class ApexPostModelTests
{
    private static readonly IModel Model = CreateModel();

    [Fact]
    public void Every_table_has_audit_columns()
    {
        // Owned types stored in their owner's row (Dimensions) share the owner's audit columns.
        var tables = Model.GetEntityTypes().Where(type => type.FindOwnership() is null || IsSeparateTable(type)).ToArray();

        Assert.NotEmpty(tables);
        Assert.All(tables, type =>
        {
            Assert.NotNull(type.FindProperty(ShadowColumns.CreatedAtUtc));
            Assert.False(type.FindProperty(ShadowColumns.CreatedBy)!.IsNullable);
            Assert.NotNull(type.FindProperty(ShadowColumns.ModifiedAtUtc));
        });
    }

    [Fact]
    public void Branch_stock_and_counter_transactions_use_rowversion_concurrency()
    {
        Assert.True(Model.FindEntityType(typeof(BranchInventoryItem))!.FindProperty(ShadowColumns.RowVersion)!.IsConcurrencyToken);
        Assert.True(Model.FindEntityType(typeof(CounterTransaction))!.FindProperty(ShadowColumns.RowVersion)!.IsConcurrencyToken);
    }

    [Fact]
    public void Identity_document_column_can_hold_only_a_masked_reference()
    {
        var verification = Model.FindEntityType(typeof(IdentityVerificationEntity))!;

        Assert.Equal(8, verification.FindProperty(nameof(IdentityVerificationEntity.MaskedDocumentReference))!.GetMaxLength());
        Assert.Contains(verification.GetCheckConstraints(), check => check.Name == "CK_IdentityVerifications_Masked");
        Assert.DoesNotContain(Model.GetEntityTypes().SelectMany(type => type.GetProperties()), property => property.Name.Contains("DocumentNumber", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(typeof(Branch), "AK_Branches_BranchCode")]
    [InlineData(typeof(Product), "UX_Products_Sku")]
    [InlineData(typeof(StaffMember), "UX_Staff_Email")]
    public void Business_keys_are_unique(Type entity, string name)
    {
        var type = Model.FindEntityType(entity)!;

        Assert.True(
            type.GetKeys().Any(key => key.GetName() == name) || type.GetIndexes().Any(index => index.IsUnique && index.GetDatabaseName() == name),
            $"{entity.Name} has no unique key or index named {name}.");
    }

    [Theory]
    [InlineData(typeof(Shipment), "CK_Shipments_WeightKg", "[WeightKg] > 0 AND [WeightKg] <= 30")]
    [InlineData(typeof(Application.Pricing.ShippingRate), "CK_ShippingRates_MaximumWeightKg", "[MaximumWeightKg] > 0 AND [MaximumWeightKg] <= 30")]
    public void Weight_limits_mirror_the_domain_acceptance_limit(Type entity, string name, string sql)
    {
        var check = Assert.Single(Model.FindEntityType(entity)!.GetCheckConstraints(), constraint => constraint.Name == name);

        Assert.Equal(sql, check.Sql);
        Assert.Equal(30, ParcelWeight.MaximumKilograms);
    }

    [Fact]
    public void Only_owned_children_cascade_on_delete()
    {
        var cascades = Model.GetEntityTypes()
            .SelectMany(type => type.GetForeignKeys())
            // An owned value stored in its owner's row (Shipments.Dimensions) has no database foreign key.
            .Where(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade && (!foreignKey.IsOwnership || IsSeparateTable(foreignKey.DeclaringEntityType)))
            .Select(foreignKey => foreignKey.DeclaringEntityType.GetTableName() ?? string.Empty)
            .Order()
            .ToArray();

        Assert.Equal(["IdentityVerifications", "Payments", "TransactionLines"], cascades);
    }

    [Fact]
    public void Money_uses_decimal_18_2_and_codes_use_varchar()
    {
        var product = Model.FindEntityType(typeof(Product))!;

        Assert.Equal("decimal(18,2)", product.FindProperty(nameof(Product.UnitPrice))!.GetColumnType());
        Assert.Equal("varchar(32)", product.FindProperty(nameof(Product.Sku))!.GetColumnType());
        Assert.Equal("nvarchar(160)", product.FindProperty(nameof(Product.Name))!.GetColumnType());
    }

    [Fact]
    public void Money_order_mapper_round_trips_the_aggregate_and_rejects_unmasked_storage()
    {
        var order = new MoneyOrder(new("MO-1"), new("BR-1"), new Money(5_000m), "Sender", "Recipient", new Money(2.5m), new("TX-1"));
        order.RecordIdentityVerification(new IdentityVerificationRecord(new("TX-1"), "Passport", MaskedIdentityDocumentReference.FromDocumentNumber("AB123456"), Domain.Enums.IdentityVerificationOutcome.Verified));

        var entity = MoneyOrderMapper.ToEntity(order);
        var restored = MoneyOrderMapper.ToDomain(entity);

        Assert.Equal("****3456", entity.IdentityVerification!.MaskedDocumentReference);
        Assert.Equal((order.Id, order.Total, order.IdentityVerificationOutcome), (restored.Id, restored.Total, restored.IdentityVerificationOutcome));

        entity.IdentityVerification.MaskedDocumentReference = "AB123456";
        Assert.Throws<ArgumentException>(() => MoneyOrderMapper.ToDomain(entity));
    }

    private static bool IsSeparateTable(IEntityType type) =>
        type.FindOwnership() is { } ownership && type.GetTableName() != ownership.PrincipalEntityType.GetTableName();

    private static IModel CreateModel()
    {
        using var context = new ApexPostDbContext(new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True")
            .Options);
        // The design-time model keeps the relational metadata (check constraints, column types) that migrations use.
        return context.GetService<IDesignTimeModel>().Model;
    }
}
