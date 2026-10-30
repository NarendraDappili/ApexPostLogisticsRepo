using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Infrastructure.Persistence.Configurations;

/// <summary>Shadow column names shared by every table; the domain model never sees them.</summary>
public static class ShadowColumns
{
    public const string CreatedAtUtc = nameof(CreatedAtUtc);
    public const string CreatedBy = nameof(CreatedBy);
    public const string ModifiedAtUtc = nameof(ModifiedAtUtc);
    public const string RowVersion = nameof(RowVersion);

    public const string SystemActor = "system";
}

internal static class ConfigurationExtensions
{
    // Column sizes match the input limits already enforced by the MVC view models and request DTOs,
    // so a value that passes validation always fits its column.
    public const int CodeLength = 48;
    public const int NameLength = 160;

    public static void HasAuditColumns<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<DateTime>(ShadowColumns.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property<string>(ShadowColumns.CreatedBy).HasMaxLength(CodeLength).IsUnicode(false).IsRequired().HasDefaultValue(ShadowColumns.SystemActor);
        builder.Property<DateTime?>(ShadowColumns.ModifiedAtUtc).HasColumnType("datetime2(3)");
    }

    public static void HasAuditColumns<TOwner, TDependent>(this OwnedNavigationBuilder<TOwner, TDependent> builder)
        where TOwner : class
        where TDependent : class
    {
        builder.Property<DateTime>(ShadowColumns.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property<string>(ShadowColumns.CreatedBy).HasMaxLength(CodeLength).IsUnicode(false).IsRequired().HasDefaultValue(ShadowColumns.SystemActor);
        builder.Property<DateTime?>(ShadowColumns.ModifiedAtUtc).HasColumnType("datetime2(3)");
    }

    /// <summary>
    /// Adds an int identity surrogate key as a shadow property. It keeps the clustered index narrow and
    /// insert-ordered, while the domain keeps addressing rows by their business code.
    /// </summary>
    public static void HasSurrogateKey<TEntity>(this EntityTypeBuilder<TEntity> builder, string name, bool large = false)
        where TEntity : class
    {
        if (large) builder.Property<long>(name).UseIdentityColumn();
        else builder.Property<int>(name).UseIdentityColumn();
        builder.HasKey(name);
    }

    /// <summary>Builds a CHECK expression from the enum names stored as text in <paramref name="column"/>.</summary>
    public static string InEnum<TEnum>(string column)
        where TEnum : struct, Enum => In(column, Enum.GetNames<TEnum>());

    // Values come from enum names and constants in code, never from user input.
    public static string In(string column, IEnumerable<string> values) =>
        $"[{column}] IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
