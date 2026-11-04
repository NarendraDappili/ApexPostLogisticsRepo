using System.Globalization;
using System.Runtime.CompilerServices;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence.Configurations;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// Records every inserted, updated and deleted row as a DataChanges row with its old and new column values. The rows
/// are added to the same SaveChanges, so they commit or roll back with the change itself (an outbox-style audit,
/// unlike the request audit of ADR 0011). Rows are identified by their business key, never by a surrogate identity.
/// <list type="bullet">
/// <item>Secret columns (the password hash) are stored as <see cref="DataChangeColumn.Hidden"/>: the history shows
/// that they changed, never their value.</item>
/// <item>The audit columns and the rowversion are left out; they change on every save and say nothing new.</item>
/// <item>Writes that bypass SaveChanges (the set-based price revision and usp_AdjustStock) add their own DataChanges
/// rows; this interceptor fills in who, where and the correlation ID that they leave empty.</item>
/// </list>
/// After a successful save every DataChanges row is also written to the audit log (<see cref="AuditTrailLogger"/>).
/// </summary>
public sealed class DataChangeAuditInterceptor(
    TimeProvider timeProvider,
    ICurrentStaffContext? currentStaff = null,
    ICorrelationContext? correlation = null,
    AuditTrailLogger? auditLog = null) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditTables = [typeof(AuditEntryEntity), typeof(DataChangeEntity), typeof(SecurityEventEntity)];
    private static readonly HashSet<string> SkippedProperties = [ShadowColumns.CreatedAtUtc, ShadowColumns.CreatedBy, ShadowColumns.ModifiedAtUtc, ShadowColumns.RowVersion];
    private static readonly HashSet<string> SecretColumns = new(StringComparer.OrdinalIgnoreCase) { "PasswordHash" };
    private static readonly string[] BranchColumns = ["BranchCode", "OriginBranchCode", "AssignedBranchCode"];
    private const int RecordKeyLength = 200;

    // The rows staged by each context's current save. The scoped interceptor is shared by every context of a request.
    private readonly ConditionalWeakTable<DbContext, PendingSave> _pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Log(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Log(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;
        var tracker = context.ChangeTracker;
        if (_pending.TryGetValue(context, out var previous))
        {
            // An earlier save of this context failed and is being repeated: its rows are rebuilt below, not doubled.
            foreach (var stale in previous.Generated.Select(context.Entry).Where(entry => entry.State == EntityState.Added)) stale.State = EntityState.Detached;
            _pending.Remove(context);
        }

        tracker.DetectChanges();
        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            var now = timeProvider.GetUtcNow();
            var staffId = currentStaff?.StaffId?.Value;
            var staffBranch = currentStaff?.BranchCode?.Value;
            var correlationId = correlation?.CorrelationId;
            var tracked = new TrackedEntries(tracker.Entries().ToList());

            var rows = new Dictionary<EntityEntry, (DataChangeEntity Row, List<DataChangeColumn> Columns)>(ReferenceEqualityComparer.Instance);
            foreach (var entry in tracked.All.Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && !AuditTables.Contains(entry.Metadata.ClrType)))
            {
                var columns = Columns(entry);
                if (columns.Count == 0) continue;
                // An owned value stored in its owner's row (a shipment's dimensions) is part of the owner's row change.
                var rowEntry = SameRowOwner(entry, tracked) ?? entry;
                if (!rows.TryGetValue(rowEntry, out var row))
                {
                    row = (new DataChangeEntity
                    {
                        OccurredAtUtc = now,
                        StaffId = staffId,
                        BranchCode = Branch(rowEntry, tracked) ?? staffBranch,
                        TableName = TableName(rowEntry),
                        RecordKey = RecordKey(rowEntry, tracked),
                        Operation = Operation(rowEntry.State).ToString(),
                        CorrelationId = correlationId,
                    }, []);
                    rows[rowEntry] = row;
                }

                row.Columns.AddRange(columns);
            }

            var generated = new List<DataChangeEntity>();
            foreach (var (row, columns) in rows.Values)
            {
                var merged = Merge(columns);
                if (merged.Count == 0) continue;
                row.ChangesJson = DataChangeJson.Serialize(merged);
                var added = context.Add(row);
                added.Property(ShadowColumns.CreatedBy).CurrentValue = staffId ?? ShadowColumns.SystemActor;
                generated.Add(row);
            }

            // Rows a set-based write staged itself only say what changed; who, where and when come from the request.
            var staged = tracker.Entries<DataChangeEntity>().Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToList();
            foreach (var row in staged.Except(generated))
            {
                row.StaffId ??= staffId;
                row.BranchCode ??= staffBranch;
                row.CorrelationId ??= correlationId;
                if (row.OccurredAtUtc == default) row.OccurredAtUtc = now;
            }

            if (staged.Count > 0) _pending.AddOrUpdate(context, new PendingSave(generated, staged));
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private void Log(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var saved)) return;
        _pending.Remove(context);
        if (auditLog is null) return;
        foreach (var row in saved.All) auditLog.DataChange(row, DataChangeJson.Deserialize(row.ChangesJson).Select(column => column.Column));
    }

    private static List<DataChangeColumn> Columns(EntityEntry entry)
    {
        var table = StoreObjectIdentifier.Create(entry.Metadata, StoreObjectType.Table);
        var columns = new List<DataChangeColumn>();
        foreach (var property in entry.Properties.Concat(entry.ComplexProperties.SelectMany(complex => complex.Properties)))
        {
            var metadata = property.Metadata;
            if (SkippedProperties.Contains(metadata.Name) || IsSurrogate(metadata) || property.IsTemporary) continue;
            var column = ColumnName(metadata, table);
            string? oldValue = null, newValue = null;
            switch (entry.State)
            {
                case EntityState.Added:
                    newValue = Format(metadata, property.CurrentValue);
                    if (newValue is null) continue;
                    break;
                case EntityState.Deleted:
                    oldValue = Format(metadata, property.OriginalValue);
                    if (oldValue is null) continue;
                    break;
                default:
                    if (!property.IsModified) continue;
                    oldValue = Format(metadata, property.OriginalValue);
                    newValue = Format(metadata, property.CurrentValue);
                    if (oldValue == newValue) continue;
                    break;
            }

            if (SecretColumns.Contains(column))
            {
                oldValue = oldValue is null ? null : DataChangeColumn.Hidden;
                newValue = newValue is null ? null : DataChangeColumn.Hidden;
            }

            columns.Add(new DataChangeColumn(column, oldValue, newValue));
        }

        return columns;
    }

    // A replaced owned value is a delete and an insert of the same columns; one column change says it better. A secret
    // that changed reads "(hidden)" on both sides, so it is kept: its real values were compared before they were hidden.
    private static List<DataChangeColumn> Merge(List<DataChangeColumn> columns) =>
        [.. columns.GroupBy(column => column.Column, StringComparer.Ordinal)
            .Select(group => new DataChangeColumn(group.Key, group.Select(column => column.Old).FirstOrDefault(value => value is not null), group.Select(column => column.New).LastOrDefault(value => value is not null)))
            .Where(column => column.Old != column.New || column.New == DataChangeColumn.Hidden)];

    /// <summary>An identity key, or a foreign key to one: numbers that mean nothing outside the database.</summary>
    private static bool IsSurrogate(IReadOnlyProperty property) =>
        (property.IsPrimaryKey() && property.ValueGenerated == ValueGenerated.OnAdd)
        || property.GetContainingForeignKeys().Any(foreignKey => foreignKey.PrincipalKey.IsPrimaryKey() && foreignKey.PrincipalKey.Properties.Any(key => key.ValueGenerated == ValueGenerated.OnAdd));

    private static string RecordKey(EntityEntry entry, TrackedEntries tracked)
    {
        var type = entry.Metadata;
        bool Usable(IReadOnlyList<IProperty> properties) => properties.All(property => !IsSurrogate(property) && !entry.Property(property.Name).IsTemporary);

        if (type.GetKeys().FirstOrDefault(key => !key.IsPrimaryKey() && Usable(key.Properties)) is { } alternateKey) return Describe(entry, alternateKey.Properties);
        if (type.GetIndexes().FirstOrDefault(index => index.IsUnique && index.GetFilter() is null && Usable(index.Properties)) is { } uniqueIndex) return Describe(entry, uniqueIndex.Properties);
        if (type.FindPrimaryKey() is { } primaryKey && Usable(primaryKey.Properties)) return Describe(entry, primaryKey.Properties);
        // A child row with only an identity key (a transaction line, a payment) is named after its parent.
        foreach (var foreignKey in type.GetForeignKeys().OrderByDescending(foreignKey => foreignKey.IsOwnership))
        {
            if (tracked.Principal(entry, foreignKey) is { } principal) return Limit($"{RecordKey(principal, tracked)} / {TableName(entry)}");
        }

        return type.FindPrimaryKey() is { } key && key.Properties.All(property => !entry.Property(property.Name).IsTemporary) ? Describe(entry, key.Properties) : "(new)";
    }

    private static string Describe(EntityEntry entry, IReadOnlyList<IProperty> properties)
    {
        var table = StoreObjectIdentifier.Create(entry.Metadata, StoreObjectType.Table);
        return Limit(string.Join(", ", properties.Select(property =>
        {
            var value = entry.Property(property.Name);
            return $"{ColumnName(property, table)}={Format(property, value.CurrentValue ?? value.OriginalValue)}";
        })));
    }

    private static string? Branch(EntityEntry entry, TrackedEntries tracked)
    {
        var table = StoreObjectIdentifier.Create(entry.Metadata, StoreObjectType.Table);
        foreach (var property in entry.Properties.Where(property => BranchColumns.Contains(ColumnName(property.Metadata, table), StringComparer.Ordinal)))
        {
            if (Format(property.Metadata, property.CurrentValue ?? property.OriginalValue) is { } branch) return branch;
        }

        // A child row (a transaction line) belongs to its parent's branch.
        return entry.Metadata.GetForeignKeys().Select(foreignKey => tracked.Principal(entry, foreignKey)).OfType<EntityEntry>().Select(principal => Branch(principal, tracked)).FirstOrDefault(branch => branch is not null);
    }

    private static EntityEntry? SameRowOwner(EntityEntry entry, TrackedEntries tracked) =>
        entry.Metadata.FindOwnership() is { } ownership && entry.Metadata.GetTableName() == ownership.PrincipalEntityType.GetTableName()
            ? tracked.Principal(entry, ownership)
            : null;

    private static DataChangeOperation Operation(EntityState state) => state switch
    {
        EntityState.Added => DataChangeOperation.Insert,
        EntityState.Deleted => DataChangeOperation.Delete,
        _ => DataChangeOperation.Update,
    };

    private static string TableName(EntityEntry entry) => entry.Metadata.GetTableName() ?? entry.Metadata.ShortName();

    private static string ColumnName(IReadOnlyProperty property, StoreObjectIdentifier? table) =>
        (table is { } storeObject ? property.GetColumnName(storeObject) : null) ?? property.GetColumnName();

    private static string? Format(IReadOnlyProperty property, object? value)
    {
        if (value is null) return null;
        // An enum stored as a number (the staff role) is clearer by name.
        if (value is Enum enumValue) return enumValue.ToString();
        var provider = property.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value;
        return provider switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            // As the column stores it: 10 in a decimal(18,2) column is 10.00.
            decimal number when property.GetScale() is { } scale => number.ToString($"F{scale}", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => provider.ToString(),
        };
    }

    private static string Limit(string value) => value.Length <= RecordKeyLength ? value : value[..RecordKeyLength];

    private sealed record PendingSave(IReadOnlyList<DataChangeEntity> Generated, IReadOnlyList<DataChangeEntity> All);

    /// <summary>The entries of one save, with a lazily built lookup from a principal's key to its entry.</summary>
    private sealed class TrackedEntries(IReadOnlyList<EntityEntry> all)
    {
        private readonly Dictionary<IReadOnlyKey, Dictionary<string, EntityEntry>> _byKey = [];

        public IReadOnlyList<EntityEntry> All { get; } = all;

        public EntityEntry? Principal(EntityEntry dependent, IReadOnlyForeignKey foreignKey)
        {
            if (!_byKey.TryGetValue(foreignKey.PrincipalKey, out var entries))
            {
                entries = [];
                foreach (var candidate in All.Where(candidate => foreignKey.PrincipalEntityType.IsAssignableFrom(candidate.Metadata)))
                    entries.TryAdd(KeyText(candidate, foreignKey.PrincipalKey.Properties), candidate);
                _byKey[foreignKey.PrincipalKey] = entries;
            }

            return entries.GetValueOrDefault(KeyText(dependent, foreignKey.Properties));
        }

        private static string KeyText(EntityEntry entry, IReadOnlyList<IReadOnlyProperty> properties) =>
            string.Join('\u001f', properties.Select(property =>
            {
                var value = entry.Property(property.Name);
                return Convert.ToString(value.CurrentValue ?? value.OriginalValue, CultureInfo.InvariantCulture);
            }));
    }
}
