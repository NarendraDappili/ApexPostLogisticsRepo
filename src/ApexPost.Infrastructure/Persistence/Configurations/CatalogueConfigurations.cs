using ApexPost.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ApexPost.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace ApexPost.Infrastructure.Persistence.Configurations;

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories");
        builder.HasSurrogateKey("ProductCategoryId");
        builder.Property(category => category.Code).HasColumnName("CategoryCode").HasMaxLength(ProductCategory.MaxCodeLength).IsUnicode(false);
        builder.HasAlternateKey(category => category.Code).HasName("AK_ProductCategories_CategoryCode");
        builder.Property(category => category.Name).HasMaxLength(80);
        builder.HasAuditColumns();
        // Products default to this category, so it must exist before any product row.
        builder.HasData(new
        {
            ProductCategoryId = 1,
            Code = ProductCategory.GeneralCode,
            Name = "General",
            CreatedAtUtc = SeedClock.ReferenceDataCreatedAtUtc,
            CreatedBy = SeedClock.MigrationActor,
        });
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table => table.HasCheckConstraint("CK_Products_UnitPrice", "[UnitPrice] >= 0"));
        builder.HasSurrogateKey("ProductId");
        builder.Property(product => product.Id).HasColumnName("ProductCode");
        builder.HasAlternateKey(product => product.Id).HasName("AK_Products_ProductCode");
        builder.Property(product => product.Sku);
        builder.HasIndex(product => product.Sku).IsUnique().HasDatabaseName("UX_Products_Sku");
        builder.Property(product => product.Name).HasMaxLength(NameLength);
        builder.ComplexProperty(product => product.UnitPrice, price => price.Property(money => money.Amount).HasColumnName("UnitPrice").HasPrecision(18, 2));
        builder.Property(product => product.CategoryCode).HasMaxLength(ProductCategory.MaxCodeLength).IsUnicode(false);
        builder.HasOne<ProductCategory>().WithMany().HasForeignKey(product => product.CategoryCode).HasPrincipalKey(category => category.Code).OnDelete(DeleteBehavior.Restrict);
        builder.HasAuditColumns();
    }
}

internal sealed class BranchStockConfiguration : IEntityTypeConfiguration<BranchInventoryItem>
{
    public void Configure(EntityTypeBuilder<BranchInventoryItem> builder)
    {
        builder.ToTable("BranchStock", table => table.HasCheckConstraint("CK_BranchStock_Quantity", "[Quantity] >= 0"));
        builder.HasSurrogateKey("BranchStockId");
        builder.Property(item => item.BranchCode);
        builder.Property(item => item.ProductId).HasColumnName("ProductCode");
        builder.HasAlternateKey(item => new { item.BranchCode, item.ProductId }).HasName("AK_BranchStock_BranchCode_ProductCode");
        builder.HasOne<Branch>().WithMany().HasForeignKey(item => item.BranchCode).HasPrincipalKey(branch => branch.Code).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).HasPrincipalKey(product => product.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.Quantity).HasDefaultValue(0).ValueGeneratedNever();
        // Low-stock report and dashboard (PERFORMANCE.md, W1/W5): equality on the branch, then the range on Quantity,
        // so one branch's low rows are a single short range. ProductCode is included to avoid a lookup per row.
        // Not filtered on a fixed threshold: the threshold is configuration and reaches SQL as a parameter.
        builder.HasIndex(item => new { item.BranchCode, item.Quantity })
            .IncludeProperties(item => item.ProductId)
            .HasDatabaseName("IX_BranchStock_BranchCode_Quantity");
        // Optimistic concurrency: two clerks selling the last unit cannot both save against the same version.
        builder.Property<byte[]>(ShadowColumns.RowVersion).IsRowVersion().IsRequired();
        builder.HasAuditColumns();
    }
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.ToTable("StockAdjustments", table => table.HasCheckConstraint("CK_StockAdjustments_Delta", "[Delta] <> 0"));
        builder.HasSurrogateKey("StockAdjustmentId", large: true);
        builder.Property(adjustment => adjustment.BranchCode);
        builder.Property(adjustment => adjustment.ProductId).HasColumnName("ProductCode");
        builder.Property(adjustment => adjustment.Delta);
        builder.Property(adjustment => adjustment.Reason).HasMaxLength(StockAdjustment.MaxReasonLength);
        builder.Property(adjustment => adjustment.ChangedBy).HasColumnName("ChangedByStaffCode");
        builder.Property(adjustment => adjustment.OccurredAtUtc);
        // History belongs to a stock row, not just to a branch and a product separately.
        builder.HasOne<BranchInventoryItem>().WithMany()
            .HasForeignKey(adjustment => new { adjustment.BranchCode, adjustment.ProductId })
            .HasPrincipalKey(item => new { item.BranchCode, item.ProductId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(adjustment => adjustment.ChangedBy).HasPrincipalKey(staff => staff.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasAuditColumns();
    }
}
