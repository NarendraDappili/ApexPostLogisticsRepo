using System.Text.Json;
using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Queries;

/// <summary>
/// Read side of the three audit tables for the audit log screen: no-tracking, newest first, one page at a time. The
/// period is a half-open UTC range on OccurredAtUtc, so every filter can seek an index (the branch, the period, the
/// correlation ID). It applies the filter it is given; <see cref="AuditLogService"/> decides who may read what.
/// </summary>
public sealed class EfAuditLogQueries(ApexPostDbContext db) : IAuditLogQueries
{
    private static readonly JsonSerializerOptions RouteValueJson = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AuditTables = ["AuditEntries", "DataChanges", "SecurityEvents"];

    public IReadOnlyList<string> AuditedTables =>
        [.. db.Model.GetEntityTypes().Select(type => type.GetTableName()).OfType<string>().Where(table => !AuditTables.Contains(table)).Distinct().Order(StringComparer.Ordinal)];

    public Task<AuditLogPage<RequestAuditItem>> GetRequestsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (from, to) = Period(filter);
        var query = db.AuditEntries.AsNoTracking().Where(entry => entry.OccurredAtUtc >= from && entry.OccurredAtUtc < to);
        if (filter.BranchCode is { } branch) query = query.Where(entry => entry.BranchCode == branch);
        if (filter.StaffId is { } staff) query = query.Where(entry => entry.StaffId == staff);
        if (filter.Kind is { } controller) query = query.Where(entry => entry.Controller == controller);
        if (filter.Result is { } outcome) query = query.Where(entry => entry.Outcome == outcome);
        if (filter.CorrelationId is { } correlation) query = query.Where(entry => entry.CorrelationId == correlation);
        return PageAsync(query.OrderByDescending(entry => entry.AuditEntryId), filter, ToItem, cancellationToken);
    }

    public Task<AuditLogPage<DataChangeItem>> GetDataChangesAsync(AuditLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (from, to) = Period(filter);
        var query = db.DataChanges.AsNoTracking().Where(change => change.OccurredAtUtc >= from && change.OccurredAtUtc < to);
        if (filter.BranchCode is { } branch) query = query.Where(change => change.BranchCode == branch);
        if (filter.StaffId is { } staff) query = query.Where(change => change.StaffId == staff);
        if (filter.Kind is { } table) query = query.Where(change => change.TableName == table);
        if (filter.Result is { } operation) query = query.Where(change => change.Operation == operation);
        if (filter.CorrelationId is { } correlation) query = query.Where(change => change.CorrelationId == correlation);
        return PageAsync(query.OrderByDescending(change => change.DataChangeId), filter, ToItem, cancellationToken);
    }

    public Task<AuditLogPage<SecurityEventItem>> GetSecurityEventsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (from, to) = Period(filter);
        var query = db.SecurityEvents.AsNoTracking().Where(securityEvent => securityEvent.OccurredAtUtc >= from && securityEvent.OccurredAtUtc < to);
        if (filter.BranchCode is { } branch) query = query.Where(securityEvent => securityEvent.BranchCode == branch);
        if (filter.StaffId is { } staff) query = query.Where(securityEvent => securityEvent.StaffId == staff);
        if (filter.Kind is { } eventType) query = query.Where(securityEvent => securityEvent.EventType == eventType);
        if (filter.CorrelationId is { } correlation) query = query.Where(securityEvent => securityEvent.CorrelationId == correlation);
        return PageAsync(query.OrderByDescending(securityEvent => securityEvent.SecurityEventId), filter, ToItem, cancellationToken);
    }

    public async Task<AuditTrace> GetTraceAsync(string correlationId, string? branchCode, bool includeSecurityEvents, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var requests = db.AuditEntries.AsNoTracking().Where(entry => entry.CorrelationId == correlationId);
        var changes = db.DataChanges.AsNoTracking().Where(change => change.CorrelationId == correlationId);
        if (branchCode is not null)
        {
            requests = requests.Where(entry => entry.BranchCode == branchCode);
            changes = changes.Where(change => change.BranchCode == branchCode);
        }

        var requestRows = await requests.OrderBy(entry => entry.AuditEntryId).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        var changeRows = await changes.OrderBy(change => change.DataChangeId).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        var securityRows = includeSecurityEvents
            ? await db.SecurityEvents.AsNoTracking().Where(securityEvent => securityEvent.CorrelationId == correlationId)
                .OrderBy(securityEvent => securityEvent.SecurityEventId).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
        return new AuditTrace(correlationId, [.. requestRows.Select(ToItem)], [.. changeRows.Select(ToItem)], [.. securityRows.Select(ToItem)]);
    }

    // UTC days, both included: [From 00:00, To + 1 day 00:00).
    private static (DateTimeOffset From, DateTimeOffset To) Period(AuditLogFilter filter) =>
        (new DateTimeOffset(filter.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static async Task<AuditLogPage<TItem>> PageAsync<TEntity, TItem>(IQueryable<TEntity> query, AuditLogFilter filter, Func<TEntity, TItem> map, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        // A page past the end (the log is filtered after a link was made) shows the last page instead of nothing.
        var page = Math.Min(filter.Page, Math.Max(1, (total + filter.PageSize - 1) / filter.PageSize));
        var rows = await query.Skip((page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AuditLogPage<TItem>([.. rows.Select(map)], page, filter.PageSize, total);
    }

    private static RequestAuditItem ToItem(AuditEntryEntity entry) => new(
        entry.AuditEntryId, entry.OccurredAtUtc, entry.StaffId, entry.Role, entry.BranchCode, entry.Controller, entry.Action,
        JsonSerializer.Deserialize<Dictionary<string, string>>(entry.RouteValuesJson, RouteValueJson) ?? [],
        entry.StatusCode, entry.DurationMilliseconds, entry.CorrelationId, entry.Outcome);

    private static DataChangeItem ToItem(DataChangeEntity change) => new(
        change.DataChangeId, change.OccurredAtUtc, change.StaffId, change.BranchCode, change.TableName, change.RecordKey,
        Enum.Parse<DataChangeOperation>(change.Operation), DataChangeJson.Deserialize(change.ChangesJson), change.CorrelationId);

    private static SecurityEventItem ToItem(SecurityEventEntity securityEvent) => new(
        securityEvent.SecurityEventId, securityEvent.OccurredAtUtc, Enum.Parse<SecurityEventType>(securityEvent.EventType), securityEvent.StaffId,
        securityEvent.Role, securityEvent.BranchCode, securityEvent.HttpMethod, securityEvent.Path, securityEvent.StatusCode,
        securityEvent.RemoteIpAddress, securityEvent.Detail, securityEvent.CorrelationId);
}
