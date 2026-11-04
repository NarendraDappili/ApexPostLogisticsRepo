using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>
/// Append-only store over the SecurityEvents table. Like <see cref="EfAuditStore"/>, every append uses its own
/// short-lived context, so a refused or failed request is recorded even when the request's own work is rolled back.
/// </summary>
public sealed class EfSecurityEventStore(DbContextOptions<ApexPostDbContext> options, AuditTrailLogger? auditLog = null) : ISecurityEventStore
{
    public async Task AppendAsync(SecurityEventRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await using var db = new ApexPostDbContext(options);
        db.SecurityEvents.Add(new SecurityEventEntity
        {
            OccurredAtUtc = record.OccurredAt,
            EventType = record.EventType.ToString(),
            StaffId = Limit(record.StaffId, 48),
            Role = Limit(record.Role, 30),
            BranchCode = Limit(record.BranchCode, 48),
            HttpMethod = Limit(record.HttpMethod, 10) ?? string.Empty,
            Path = Limit(record.Path, 256) ?? "/",
            StatusCode = record.StatusCode,
            RemoteIpAddress = Limit(record.RemoteIpAddress, 45),
            Detail = Limit(record.Detail, 200),
            CorrelationId = Limit(record.CorrelationId, 64) ?? string.Empty,
        });
        // The event has already happened; once started, the write finishes even if the client disconnects.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        auditLog?.Security(record);
    }

    private static string? Limit(string? value, int length) => value is null ? null : value[..Math.Min(value.Length, length)];
}
