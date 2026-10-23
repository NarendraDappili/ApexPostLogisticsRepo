using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Auditing;

/// <summary>
/// Describes an audit event. Summaries must not contain identity-document numbers, credentials, or payment-card data.
/// </summary>
public sealed record AuditRecord(
    string EventType,
    DateTimeOffset OccurredAt,
    StaffId? ActorId,
    string Summary);
