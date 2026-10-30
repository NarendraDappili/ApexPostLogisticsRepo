using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Infrastructure.Persistence.Configurations;

internal sealed class ShippingZoneConfiguration : IEntityTypeConfiguration<ShippingZoneEntity>
{
    public void Configure(EntityTypeBuilder<ShippingZoneEntity> builder)
    {
        builder.ToTable("ShippingZones");
        builder.HasSurrogateKey("ShippingZoneId");
        builder.HasAlternateKey(zone => zone.ZoneCode).HasName("AK_ShippingZones_ZoneCode");
        builder.Property(zone => zone.Name).HasMaxLength(80);
        builder.HasAuditColumns();
    }
}

internal sealed class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> builder)
    {
        builder.ToTable("ShippingRates", table =>
        {
            table.HasCheckConstraint("CK_ShippingRates_Charge", "[Charge] > 0");
            table.HasCheckConstraint("CK_ShippingRates_MaximumWeightKg", $"[MaximumWeightKg] > 0 AND [MaximumWeightKg] <= {ParcelWeight.MaximumKilograms}");
            table.HasCheckConstraint("CK_ShippingRates_ShipmentType", ConfigurationExtensions.InEnum<ShipmentType>("ShipmentType"));
            table.HasCheckConstraint("CK_ShippingRates_ServiceLevel", ConfigurationExtensions.InEnum<ShippingServiceLevel>("ServiceLevel"));
        });
        builder.HasSurrogateKey("ShippingRateId");
        builder.Property(rate => rate.DestinationZoneCode).HasColumnName("ZoneCode");
        builder.Property(rate => rate.ShipmentType);
        builder.Property(rate => rate.ServiceLevel);
        builder.Property(rate => rate.Charge);
        builder.Property(rate => rate.MaximumWeight).HasColumnName("MaximumWeightKg");
        builder.HasOne<ShippingZoneEntity>().WithMany().HasForeignKey(rate => rate.DestinationZoneCode).HasPrincipalKey(zone => zone.ZoneCode).OnDelete(DeleteBehavior.Restrict);
        // One price per weight band of a zone/type/service combination.
        builder.HasIndex(rate => new { rate.DestinationZoneCode, rate.ShipmentType, rate.ServiceLevel, rate.MaximumWeight })
            .IsUnique()
            .HasDatabaseName("UX_ShippingRates_Band");
        builder.HasAuditColumns();
    }
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments", table =>
        {
            // Mirrors the domain acceptance limit in ParcelWeight; per-service limits stay in the rate table.
            table.HasCheckConstraint("CK_Shipments_WeightKg", $"[WeightKg] > 0 AND [WeightKg] <= {ParcelWeight.MaximumKilograms}");
            table.HasCheckConstraint("CK_Shipments_Dimensions", "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0");
            table.HasCheckConstraint("CK_Shipments_ShipmentType", ConfigurationExtensions.InEnum<ShipmentType>("ShipmentType"));
            table.HasCheckConstraint("CK_Shipments_ServiceLevel", ConfigurationExtensions.InEnum<ShippingServiceLevel>("ServiceLevel"));
        });
        builder.HasSurrogateKey("ShipmentId", large: true);
        builder.Property(shipment => shipment.Id).HasColumnName("ShipmentCode");
        builder.HasAlternateKey(shipment => shipment.Id).HasName("AK_Shipments_ShipmentCode");
        builder.HasOne<Branch>().WithMany().HasForeignKey(shipment => shipment.OriginBranchCode).HasPrincipalKey(branch => branch.Code).OnDelete(DeleteBehavior.Restrict);
        builder.Property(shipment => shipment.DestinationZoneCode).HasColumnName("ZoneCode");
        builder.HasOne<ShippingZoneEntity>().WithMany().HasForeignKey(shipment => shipment.DestinationZoneCode).HasPrincipalKey(zone => zone.ZoneCode).OnDelete(DeleteBehavior.Restrict);
        builder.Property(shipment => shipment.OriginBranchCode);
        builder.Property(shipment => shipment.Weight).HasColumnName("WeightKg");
        builder.Property(shipment => shipment.ShipmentType);
        builder.Property(shipment => shipment.ServiceLevel);
        // Dimensions are a value with no identity of their own: an owned type stored in the shipment row.
        builder.OwnsOne(shipment => shipment.Dimensions, dimensions =>
        {
            dimensions.Property(value => value.LengthCentimetres).HasColumnName("LengthCm").HasPrecision(9, 2);
            dimensions.Property(value => value.WidthCentimetres).HasColumnName("WidthCm").HasPrecision(9, 2);
            dimensions.Property(value => value.HeightCentimetres).HasColumnName("HeightCm").HasPrecision(9, 2);
        });
        builder.Navigation(shipment => shipment.Dimensions).IsRequired();
        // No FK: the clerk reserves the transaction ID for a shipment before the sale that bills it exists.
        builder.Property(shipment => shipment.TransactionId).HasColumnName("TransactionCode");
        builder.HasAuditColumns();
    }
}
