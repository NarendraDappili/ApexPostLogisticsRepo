using System.Text.Json;
using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>
/// Append-only audit store over the AuditEntries table. Every append uses its own short-lived ApexPostDbContext,
/// and so its own connection and transaction, never the request's unit of work:
/// <list type="bullet">
/// <item>a business change that failed or was rolled back stays out of the audit commit (a shared context would
/// still track it and try to save it again), while the failed attempt itself is audited;</item>
/// <item>an audit write that fails cannot undo a business change that was already committed.</item>
/// </list>
/// The store offers no update or delete.
/// </summary>
public sealed class EfAuditStore(DbContextOptions<ApexPostDbContext> options) : IAuditStore
{
    private static readonly JsonSerializerOptions RouteValueJson = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await using var db = new ApexPostDbContext(options);
        db.AuditEntries.Add(new AuditEntryEntity
        {
            OccurredAtUtc = record.OccurredAt,
            StaffId = record.StaffId,
            Role = record.Role,
            BranchCode = record.BranchCode,
            Controller = record.Controller,
            Action = record.Action,
            RouteValuesJson = JsonSerializer.Serialize(record.RouteValues, RouteValueJson),
            StatusCode = record.StatusCode,
            DurationMilliseconds = record.DurationMilliseconds,
            CorrelationId = record.CorrelationId,
            Outcome = record.Outcome,
        });
        // The audited action has already happened; once started, the write finishes even if the client disconnects.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new ApexPostDbContext(options);
        var entries = await db.AuditEntries.AsNoTracking().OrderBy(entry => entry.AuditEntryId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return entries.Select(entry => new AuditRecord(
            entry.OccurredAtUtc, entry.StaffId, entry.Role, entry.BranchCode, entry.Controller, entry.Action,
            JsonSerializer.Deserialize<Dictionary<string, string>>(entry.RouteValuesJson, RouteValueJson) ?? [],
            entry.StatusCode, entry.DurationMilliseconds, entry.CorrelationId, entry.Outcome)).ToArray();
    }
}
