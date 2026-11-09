using System.Globalization;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ApexPost.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace ApexPost.Infrastructure.Persistence.Configurations;

// The tracking API's tables (Week 4). They are new tables beside the counter's Shipments table, which is unchanged.

internal sealed class TrackedShipmentConfiguration : IEntityTypeConfiguration<TrackedShipment>
{
    public const int StatusLength = 16;

    public void Configure(EntityTypeBuilder<TrackedShipment> builder)
    {
        builder.ToTable("TrackedShipments", table =>
        {
            // Mirrors the domain limits in TrackedShipment. Invariant, so 0.1 is never written as 0,1.
            table.HasCheckConstraint("CK_TrackedShipments_WeightKg",
                string.Create(CultureInfo.InvariantCulture, $"[WeightKg] >= {TrackedShipment.MinimumWeightKg} AND [WeightKg] <= {TrackedShipment.MaximumWeightKg}"));
            table.HasCheckConstraint("CK_TrackedShipments_Status", InEnum<TrackingStatus>("Status"));
        });
        builder.HasSurrogateKey("TrackedShipmentId", large: true);
        builder.Property(shipment => shipment.TrackingNumber).HasConversion<TrackingNumberConverter>().HasMaxLength(TrackingNumber.Length).IsUnicode(false);
        builder.HasAlternateKey(shipment => shipment.TrackingNumber).HasName("AK_TrackedShipments_TrackingNumber");
        builder.Property(shipment => shipment.WeightKg).HasPrecision(6, 3);
        builder.Property(shipment => shipment.Status).HasConversion<string>().HasMaxLength(StatusLength).IsUnicode(false);
        // The list filtered by status (GET /api/shipments?status=): a seek on the status, rows already in page order.
        builder.HasIndex(shipment => new { shipment.Status, shipment.TrackingNumber }).HasDatabaseName("IX_TrackedShipments_Status_TrackingNumber");
        // The recipient is a value with no identity of its own: an owned type stored in the shipment row.
        builder.OwnsOne(shipment => shipment.Recipient, recipient =>
        {
            recipient.Property(value => value.Name).HasColumnName("RecipientName").HasMaxLength(Recipient.NameMaxLength);
            recipient.Property(value => value.Phone).HasColumnName("RecipientPhone").HasMaxLength(Recipient.PhoneMaxLength).IsUnicode(false);
            recipient.Property(value => value.Address).HasColumnName("RecipientAddress").HasMaxLength(Recipient.AddressMaxLength);
            recipient.Property(value => value.City).HasColumnName("City").HasMaxLength(Recipient.CityMaxLength);
            // The list filtered by city (GET /api/shipments?city=).
            recipient.HasIndex(value => value.City).HasDatabaseName("IX_TrackedShipments_City");
        });
        builder.Navigation(shipment => shipment.Recipient).IsRequired();
        // Optimistic concurrency: a status change and a details replace that read the same version cannot both save.
        builder.Property<byte[]>(ShadowColumns.RowVersion).IsRowVersion().IsRequired();
        builder.HasAuditColumns();
    }
}

internal sealed class TrackingScanConfiguration : IEntityTypeConfiguration<TrackingScan>
{
    public void Configure(EntityTypeBuilder<TrackingScan> builder)
    {
        builder.ToTable("TrackingScans", table => table.HasCheckConstraint("CK_TrackingScans_ScanType", InEnum<TrackingScanType>("ScanType")));
        builder.HasSurrogateKey("TrackingScanId", large: true);
        builder.Property(scan => scan.Id).HasColumnName("ScanId").HasConversion<TrackingScanIdConverter>().HasMaxLength(TrackingScanId.Length).IsUnicode(false);
        // The device's scan ID is unique: it is what makes a repeated upload recognisable (idempotent scan upload).
        builder.HasAlternateKey(scan => scan.Id).HasName("AK_TrackingScans_ScanId");
        builder.Property(scan => scan.TrackingNumber).HasConversion<TrackingNumberConverter>().HasMaxLength(TrackingNumber.Length).IsUnicode(false);
        builder.HasOne<TrackedShipment>().WithMany()
            .HasForeignKey(scan => scan.TrackingNumber)
            .HasPrincipalKey(shipment => shipment.TrackingNumber)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(scan => scan.ScanType).HasConversion<string>().HasMaxLength(12).IsUnicode(false);
        builder.Property(scan => scan.Location).HasMaxLength(TrackingScan.LocationMaxLength);
        builder.Property(scan => scan.CourierId).HasMaxLength(TrackingScan.CourierIdMaxLength);
        builder.Property(scan => scan.ScannedAtUtc);
        builder.Property(scan => scan.Notes).HasMaxLength(TrackingScan.NotesMaxLength);
        builder.Property(scan => scan.ReceivedAtUtc);
        // A shipment's scan history in time order, and the latest scan of each shipment on a list page: one range seek
        // per shipment, already sorted (ReceivedAtUtc breaks ties). Covering, because a lookup per scan made a 20-row
        // page read 745 pages instead of about 100; scans never change, so the copy costs only space. It starts with
        // the FK column, so it also replaces EF's single-column FK index on TrackingNumber.
        builder.HasIndex(scan => new { scan.TrackingNumber, scan.ScannedAtUtc, scan.ReceivedAtUtc })
            .IncludeProperties(scan => new { scan.Id, scan.ScanType, scan.Location, scan.CourierId, scan.Notes })
            .HasDatabaseName("IX_TrackingScans_TrackingNumber_ScannedAtUtc");
        builder.HasAuditColumns();
    }
}
