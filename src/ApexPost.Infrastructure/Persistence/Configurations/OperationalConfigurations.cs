using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ApexPost.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace ApexPost.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntryEntity>
{
    public void Configure(EntityTypeBuilder<AuditEntryEntity> builder)
    {
        builder.ToTable("AuditEntries", table =>
        {
            table.HasCheckConstraint("CK_AuditEntries_Outcome", In("Outcome", ["Succeeded", "Failed"]));
            table.HasCheckConstraint("CK_AuditEntries_StatusCode", "[StatusCode] BETWEEN 100 AND 599");
            table.HasCheckConstraint("CK_AuditEntries_DurationMilliseconds", "[DurationMilliseconds] >= 0");
            table.HasCheckConstraint("CK_AuditEntries_RouteValuesJson", "ISJSON([RouteValuesJson]) = 1");
        });
        builder.HasKey(entry => entry.AuditEntryId);
        // No foreign keys on purpose: the audit trail must also record failed sign-ins for unknown staff IDs,
        // and it must survive any later change to the staff or branch rows it mentions.
        builder.Property(entry => entry.StaffId).HasColumnName("StaffCode").HasMaxLength(CodeLength).IsUnicode(false);
        builder.Property(entry => entry.Role).HasMaxLength(30).IsUnicode(false);
        builder.Property(entry => entry.BranchCode).HasMaxLength(CodeLength).IsUnicode(false);
        builder.Property(entry => entry.Controller).HasMaxLength(100).IsUnicode(false);
        builder.Property(entry => entry.Action).HasMaxLength(100).IsUnicode(false);
        builder.Property(entry => entry.RouteValuesJson).HasMaxLength(1000);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.Property(entry => entry.Outcome).HasMaxLength(10).IsUnicode(false);
        builder.HasAuditColumns();
    }
}
