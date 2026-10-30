using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence.Conversions;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// The SQL Server system of record. Mapping lives in the IEntityTypeConfiguration classes under
/// Persistence/Configurations; this class only declares the sets and the type-wide conventions.
/// </summary>
public sealed class ApexPostDbContext(DbContextOptions<ApexPostDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Counter> Counters => Set<Counter>();

    public DbSet<RoleEntity> Roles => Set<RoleEntity>();

    public DbSet<StaffMember> Staff => Set<StaffMember>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<BranchInventoryItem> BranchStock => Set<BranchInventoryItem>();

    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    public DbSet<ShippingZoneEntity> ShippingZones => Set<ShippingZoneEntity>();

    public DbSet<Application.Pricing.ShippingRate> ShippingRates => Set<Application.Pricing.ShippingRate>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<MoneyOrderEntity> MoneyOrders => Set<MoneyOrderEntity>();

    public DbSet<CounterTransaction> CounterTransactions => Set<CounterTransaction>();

    public DbSet<AuditEntryEntity> AuditEntries => Set<AuditEntryEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Codes are short ASCII identifiers: varchar keeps them half the size of nvarchar and avoids implicit conversions.
        Code<BranchCode, BranchCodeConverter>(configurationBuilder);
        Code<CounterCode, CounterCodeConverter>(configurationBuilder);
        Code<StaffId, StaffIdConverter>(configurationBuilder);
        Code<ProductId, ProductIdConverter>(configurationBuilder);
        Code<ShipmentId, ShipmentIdConverter>(configurationBuilder);
        Code<ZoneCode, ZoneCodeConverter>(configurationBuilder);
        Code<CounterTransactionId, CounterTransactionIdConverter>(configurationBuilder);
        Code<MoneyOrderId, MoneyOrderIdConverter>(configurationBuilder);
        Code<MoneyOrderContextId, MoneyOrderContextIdConverter>(configurationBuilder);
        configurationBuilder.Properties<Sku>().HaveConversion<SkuConverter>().HaveMaxLength(Sku.MaxLength).AreUnicode(false);

        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<ParcelWeight>().HaveConversion<ParcelWeightConverter>().HavePrecision(9, 3);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>().HaveColumnType("datetime2(3)");

        // Statuses are stored by name so reports and stored procedures read naturally; CHECK constraints pin the allowed names.
        Status<CounterTransactionStatus>(configurationBuilder, 10);
        Status<PaymentMethod>(configurationBuilder, 16);
        Status<ShipmentType>(configurationBuilder, 10);
        Status<ShippingServiceLevel>(configurationBuilder, 12);
        Status<IdentityVerificationOutcome>(configurationBuilder, 10);
        configurationBuilder.Properties<StaffRole>().HaveConversion<byte>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApexPostDbContext).Assembly);

    private static void Code<TCode, TConverter>(ModelConfigurationBuilder builder) =>
        builder.Properties<TCode>().HaveConversion<TConverter>().HaveMaxLength(Configurations.ConfigurationExtensions.CodeLength).AreUnicode(false);

    private static void Status<TEnum>(ModelConfigurationBuilder builder, int maxLength)
        where TEnum : struct, Enum =>
        builder.Properties<TEnum>().HaveConversion<string>().HaveMaxLength(maxLength).AreUnicode(false);
}
