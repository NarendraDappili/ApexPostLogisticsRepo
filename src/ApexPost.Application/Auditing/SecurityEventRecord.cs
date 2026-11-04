namespace ApexPost.Application.Auditing;

/// <summary>
/// One sign-in or access event. It never carries a password, a request body or a query string; for a failed
/// sign-in <see cref="StaffId"/> is the staff ID that was typed, and only when it belongs to a real staff member.
/// </summary>
public sealed record SecurityEventRecord(
    DateTimeOffset OccurredAt,
    SecurityEventType EventType,
    string? StaffId,
    string? Role,
    string? BranchCode,
    string HttpMethod,
    string Path,
    int StatusCode,
    string? RemoteIpAddress,
    string? Detail,
    string CorrelationId);

/// <summary>Stores security events through an append operation only; it offers no update or delete.</summary>
public interface ISecurityEventStore
{
    Task AppendAsync(SecurityEventRecord record, CancellationToken cancellationToken = default);
}

/// <summary>One changed column of a data change. Secret values are stored as <see cref="Hidden"/>.</summary>
public sealed record DataChangeColumn(string Column, string? Old, string? New)
{
    public const string Hidden = "(hidden)";
}
