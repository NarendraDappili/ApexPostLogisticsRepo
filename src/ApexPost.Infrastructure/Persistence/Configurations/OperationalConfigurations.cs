using ApexPost.Application.Auditing;
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
        // The audit log screen: a Branch Manager reads their own branch for a period, an Auditor or Administrator
        // every branch for a period, and the trace view one request by its correlation ID.
        builder.HasIndex(entry => new { entry.BranchCode, entry.OccurredAtUtc }).HasDatabaseName("IX_AuditEntries_BranchCode_OccurredAtUtc");
        builder.HasIndex(entry => entry.OccurredAtUtc).HasDatabaseName("IX_AuditEntries_OccurredAtUtc");
        builder.HasIndex(entry => entry.CorrelationId).HasDatabaseName("IX_AuditEntries_CorrelationId");
        builder.HasAuditColumns();
    }
}

internal sealed class DataChangeConfiguration : IEntityTypeConfiguration<DataChangeEntity>
{
    public void Configure(EntityTypeBuilder<DataChangeEntity> builder)
    {
        builder.ToTable("DataChanges", table =>
        {
            table.HasCheckConstraint("CK_DataChanges_Operation", InEnum<DataChangeOperation>("Operation"));
            table.HasCheckConstraint("CK_DataChanges_ChangesJson", "ISJSON([ChangesJson]) = 1");
        });
        builder.HasKey(change => change.DataChangeId);
        // No foreign keys, as for AuditEntries: the history must outlive the rows and staff it describes.
        builder.Property(change => change.StaffId).HasColumnName("StaffCode").HasMaxLength(CodeLength).IsUnicode(false);
        builder.Property(change => change.BranchCode).HasMaxLength(CodeLength).IsUnicode(false);
        builder.Property(change => change.TableName).HasMaxLength(128).IsUnicode(false);
        builder.Property(change => change.RecordKey).HasMaxLength(200);
        builder.Property(change => change.Operation).HasMaxLength(10).IsUnicode(false);
        builder.Property(change => change.ChangesJson).HasColumnType("nvarchar(max)");
        builder.Property(change => change.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(change => new { change.BranchCode, change.OccurredAtUtc }).HasDatabaseName("IX_DataChanges_BranchCode_OccurredAtUtc");
        // The history of one record, such as every change to one staff member.
        builder.HasIndex(change => new { change.TableName, change.RecordKey }).HasDatabaseName("IX_DataChanges_TableName_RecordKey");
        builder.HasIndex(change => change.OccurredAtUtc).HasDatabaseName("IX_DataChanges_OccurredAtUtc");
        builder.HasIndex(change => change.CorrelationId).HasDatabaseName("IX_DataChanges_CorrelationId");
        builder.HasAuditColumns();
    }
}

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEventEntity>
{
    public void Configure(EntityTypeBuilder<SecurityEventEntity> builder)
    {
        builder.ToTable("SecurityEvents", table =>
        {
            table.HasCheckConstraint("CK_SecurityEvents_EventType", InEnum<SecurityEventType>("EventType"));
            table.HasCheckConstraint("CK_SecurityEvents_StatusCode", "[StatusCode] BETWEEN 100 AND 599");
        });
        builder.HasKey(securityEvent => securityEvent.SecurityEventId);
        // No foreign keys: a failed sign-in records the staff ID that was typed, which may not exist.
        builder.Property(securityEvent => securityEvent.EventType).HasMaxLength(20).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.StaffId).HasColumnName("StaffCode").HasMaxLength(CodeLength).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.Role).HasMaxLength(30).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.BranchCode).HasMaxLength(CodeLength).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.HttpMethod).HasMaxLength(10).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.Path).HasMaxLength(256);
        // An IPv6 address in text form is at most 45 characters.
        builder.Property(securityEvent => securityEvent.RemoteIpAddress).HasMaxLength(45).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.Detail).HasMaxLength(200);
        builder.Property(securityEvent => securityEvent.CorrelationId).HasMaxLength(64).IsUnicode(false);
        // Repeated failed sign-ins for one staff ID, and every event of one type in a period.
        builder.HasIndex(securityEvent => new { securityEvent.StaffId, securityEvent.OccurredAtUtc }).HasDatabaseName("IX_SecurityEvents_StaffCode_OccurredAtUtc");
        builder.HasIndex(securityEvent => new { securityEvent.EventType, securityEvent.OccurredAtUtc }).HasDatabaseName("IX_SecurityEvents_EventType_OccurredAtUtc");
        builder.HasIndex(securityEvent => securityEvent.OccurredAtUtc).HasDatabaseName("IX_SecurityEvents_OccurredAtUtc");
        builder.HasIndex(securityEvent => securityEvent.CorrelationId).HasDatabaseName("IX_SecurityEvents_CorrelationId");
        builder.HasAuditColumns();
    }
}
