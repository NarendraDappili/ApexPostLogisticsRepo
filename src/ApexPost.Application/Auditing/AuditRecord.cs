namespace ApexPost.Application.Auditing;

/// <summary>
/// An append-only record of an MVC action. Route values are an allow-listed operational
/// subset and must never contain credentials, identity-document numbers, or card data.
/// </summary>
public sealed record AuditRecord(
    DateTimeOffset OccurredAt,
    string? StaffId,
    string? Role,
    string? BranchCode,
    string Controller,
    string Action,
    IReadOnlyDictionary<string, string> RouteValues,
    int StatusCode,
    long DurationMilliseconds,
    string CorrelationId,
    string Outcome);
