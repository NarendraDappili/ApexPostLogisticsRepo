using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ApexPost.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace ApexPost.Infrastructure.Persistence.Configurations;

// Delete behaviour for business data is Restrict throughout: branches, staff and products are deactivated
// or closed, never deleted, so a delete that would orphan sales or audit history must fail instead.

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasSurrogateKey("BranchId");
        builder.Property(branch => branch.Code).HasColumnName("BranchCode");
        builder.HasAlternateKey(branch => branch.Code).HasName("AK_Branches_BranchCode");
        builder.Property(branch => branch.Name).HasMaxLength(NameLength);
        builder.Property(branch => branch.Region).HasMaxLength(80);
        builder.Property(branch => branch.OpeningHours).HasMaxLength(120);
        // The application always sends IsOpen; the DEFAULT covers rows inserted by scripts.
        builder.Property(branch => branch.IsOpen).HasDefaultValue(true).ValueGeneratedNever();
        builder.HasAuditColumns();
    }
}

internal sealed class CounterConfiguration : IEntityTypeConfiguration<Counter>
{
    public void Configure(EntityTypeBuilder<Counter> builder)
    {
        builder.ToTable("Counters");
        builder.HasSurrogateKey("CounterId");
        builder.Property(counter => counter.Code).HasColumnName("CounterCode");
        builder.Property(counter => counter.BranchCode);
        // Counter codes repeat across branches (every branch has C-01), so the business key is the pair.
        builder.HasAlternateKey(counter => new { counter.BranchCode, counter.Code }).HasName("AK_Counters_BranchCode_CounterCode");
        builder.HasOne<Branch>().WithMany().HasForeignKey(counter => counter.BranchCode).HasPrincipalKey(branch => branch.Code).OnDelete(DeleteBehavior.Restrict);
        builder.Property(counter => counter.IsOpen).HasDefaultValue(true).ValueGeneratedNever();
        builder.HasAuditColumns();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<RoleEntity>
{
    public void Configure(EntityTypeBuilder<RoleEntity> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(role => role.RoleId);
        builder.Property(role => role.RoleId).ValueGeneratedNever();
        builder.Property(role => role.Name).HasMaxLength(30).IsUnicode(false);
        builder.HasIndex(role => role.Name).IsUnique().HasDatabaseName("UX_Roles_Name");
        builder.HasAuditColumns();
        builder.HasData(Enum.GetValues<StaffRole>().Select(role => new
        {
            RoleId = role,
            Name = role.ToString(),
            CreatedAtUtc = SeedClock.ReferenceDataCreatedAtUtc,
            CreatedBy = SeedClock.MigrationActor,
        }));
    }
}

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("Staff");
        builder.HasSurrogateKey("StaffMemberId");
        builder.Property(staff => staff.Id).HasColumnName("StaffCode");
        builder.HasAlternateKey(staff => staff.Id).HasName("AK_Staff_StaffCode");
        builder.Property(staff => staff.Name).HasMaxLength(NameLength);
        builder.Property(staff => staff.Email).HasMaxLength(254).IsUnicode(false);
        // Email is optional for legacy rows, so uniqueness applies only to rows that have one.
        builder.HasIndex(staff => staff.Email).IsUnique().HasFilter("[Email] IS NOT NULL").HasDatabaseName("UX_Staff_Email");
        builder.Property(staff => staff.Role).HasColumnName("RoleId");
        builder.HasOne<RoleEntity>().WithMany().HasForeignKey(staff => staff.Role).OnDelete(DeleteBehavior.Restrict);
        builder.Property(staff => staff.AssignedBranchCode).HasColumnName("BranchCode");
        builder.HasOne<Branch>().WithMany().HasForeignKey(staff => staff.AssignedBranchCode).HasPrincipalKey(branch => branch.Code).OnDelete(DeleteBehavior.Restrict);
        builder.Property(staff => staff.PasswordHash).HasMaxLength(200).IsUnicode(false);
        builder.Property(staff => staff.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        builder.HasAuditColumns();
    }
}

/// <summary>Fixed values for rows seeded by migrations, so a re-generated migration does not see a model change.</summary>
internal static class SeedClock
{
    public static readonly DateTime ReferenceDataCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public const string MigrationActor = "migration";
}
