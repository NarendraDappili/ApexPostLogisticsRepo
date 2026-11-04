using ApexPost.Domain.Enums;

namespace ApexPost.Application.Auditing;

/// <summary>
/// What the audit log screen asks for: a period of UTC days (both included) and optional filters. <see cref="Kind"/>
/// is the controller of a request, the table of a data change or the type of a security event; <see cref="Result"/>
/// is a request's outcome or a data change's operation.
/// </summary>
public sealed record AuditLogFilter(
    DateOnly From,
    DateOnly To,
    string? BranchCode = null,
    string? StaffId = null,
    string? Kind = null,
    string? Result = null,
    string? CorrelationId = null,
    int Page = 1,
    int PageSize = AuditLogFilter.DefaultPageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxDays = 366;

    /// <summary>The reason a period cannot be read, or null when it can.</summary>
    public static string? FindProblem(DateOnly from, DateOnly to) =>
        from > to ? "The start date must be on or before the end date."
        : to.DayNumber - from.DayNumber >= MaxDays ? $"The period can be at most {MaxDays} days."
        : null;

    /// <summary>The filter with blank values removed, codes upper-cased and the page kept in range.</summary>
    public AuditLogFilter Normalized() => this with
    {
        BranchCode = Code(BranchCode),
        StaffId = Code(StaffId),
        Kind = Text(Kind),
        Result = Text(Result),
        CorrelationId = Text(CorrelationId),
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize),
    };

    private static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AuditLogPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}

public sealed record RequestAuditItem(
    long Id, DateTimeOffset OccurredAt, string? StaffId, string? Role, string? BranchCode, string Controller, string Action,
    IReadOnlyDictionary<string, string> RouteValues, int StatusCode, long DurationMilliseconds, string CorrelationId, string Outcome);

public sealed record DataChangeItem(
    long Id, DateTimeOffset OccurredAt, string? StaffId, string? BranchCode, string TableName, string RecordKey,
    DataChangeOperation Operation, IReadOnlyList<DataChangeColumn> Columns, string? CorrelationId);

public sealed record SecurityEventItem(
    long Id, DateTimeOffset OccurredAt, SecurityEventType EventType, string? StaffId, string? Role, string? BranchCode,
    string HttpMethod, string Path, int StatusCode, string? RemoteIpAddress, string? Detail, string CorrelationId);

/// <summary>Everything one request left in the audit trail, found by its correlation ID.</summary>
public sealed record AuditTrace(string CorrelationId, IReadOnlyList<RequestAuditItem> Requests, IReadOnlyList<DataChangeItem> DataChanges, IReadOnlyList<SecurityEventItem> SecurityEvents);

/// <summary>Read side of the audit trail. It applies the filter it is given; who may read what is decided by <see cref="AuditLogService"/>.</summary>
public interface IAuditLogQueries
{
    /// <summary>The tables whose changes are recorded, for the table filter.</summary>
    IReadOnlyList<string> AuditedTables { get; }

    Task<AuditLogPage<RequestAuditItem>> GetRequestsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);

    Task<AuditLogPage<DataChangeItem>> GetDataChangesAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);

    Task<AuditLogPage<SecurityEventItem>> GetSecurityEventsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="limit"/> rows of each kind; the security events only when asked for.</summary>
    Task<AuditTrace> GetTraceAsync(string correlationId, string? branchCode, bool includeSecurityEvents, int limit, CancellationToken cancellationToken = default);
}

/// <summary>The person reading the audit log: their role and home branch, from their sign-in.</summary>
public sealed record AuditViewer(StaffRole Role, string? HomeBranchCode)
{
    /// <summary>Auditors and Administrators read every branch; a Branch Manager reads their own branch only.</summary>
    public bool CanViewAllBranches => Role is StaffRole.Auditor or StaffRole.Administrator;

    /// <summary>Sign-ins and refusals are for Auditors and Administrators only.</summary>
    public bool CanViewSecurityEvents => Role is StaffRole.Auditor or StaffRole.Administrator;

    public bool CanViewAuditLog => CanViewAllBranches || Role == StaffRole.BranchManager;
}

/// <summary>A viewer asked for audit records their role does not allow.</summary>
public sealed class AuditLogAccessDeniedException(string message) : InvalidOperationException(message);

/// <summary>
/// The audit log screen's rules, applied on the server whatever the request says:
/// <list type="bullet">
/// <item>Auditors and Administrators read every branch and every kind of record.</item>
/// <item>A Branch Manager reads the requests and data changes of their own branch only, never the security events;
/// asking for another branch is refused, not silently widened or narrowed.</item>
/// <item>Counter Clerks have no access.</item>
/// </list>
/// The log is read-only: there is nothing here, or in <see cref="IAuditLogQueries"/>, that changes it.
/// </summary>
public sealed class AuditLogService(IAuditLogQueries queries)
{
    public const int TraceLimit = 200;

    public IReadOnlyList<string> AuditedTables => queries.AuditedTables;

    public Task<AuditLogPage<RequestAuditItem>> GetRequestsAsync(AuditViewer viewer, AuditLogFilter filter, CancellationToken cancellationToken = default) =>
        queries.GetRequestsAsync(Scope(viewer, filter), cancellationToken);

    public Task<AuditLogPage<DataChangeItem>> GetDataChangesAsync(AuditViewer viewer, AuditLogFilter filter, CancellationToken cancellationToken = default) =>
        queries.GetDataChangesAsync(Scope(viewer, filter), cancellationToken);

    public Task<AuditLogPage<SecurityEventItem>> GetSecurityEventsAsync(AuditViewer viewer, AuditLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (!viewer.CanViewSecurityEvents) throw new AuditLogAccessDeniedException("Only Auditors and Administrators can read security events.");
        return queries.GetSecurityEventsAsync(Scope(viewer, filter), cancellationToken);
    }

    public Task<AuditTrace> GetTraceAsync(AuditViewer viewer, string correlationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        EnsureCanView(viewer);
        return queries.GetTraceAsync(correlationId.Trim(), viewer.CanViewAllBranches ? null : HomeBranch(viewer), viewer.CanViewSecurityEvents, TraceLimit, cancellationToken);
    }

    private static AuditLogFilter Scope(AuditViewer viewer, AuditLogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(filter);
        EnsureCanView(viewer);
        if (AuditLogFilter.FindProblem(filter.From, filter.To) is { } problem) throw new ArgumentException(problem, nameof(filter));
        var normalized = filter.Normalized();
        if (viewer.CanViewAllBranches) return normalized;

        var home = HomeBranch(viewer);
        if (normalized.BranchCode is not null && normalized.BranchCode != home)
            throw new AuditLogAccessDeniedException("A Branch Manager can only read the audit log of their own branch.");
        return normalized with { BranchCode = home };
    }

    private static void EnsureCanView(AuditViewer viewer)
    {
        if (!viewer.CanViewAuditLog) throw new AuditLogAccessDeniedException("Your role cannot read the audit log.");
    }

    private static string HomeBranch(AuditViewer viewer) =>
        string.IsNullOrWhiteSpace(viewer.HomeBranchCode)
            ? throw new AuditLogAccessDeniedException("Your account has no home branch.")
            : viewer.HomeBranchCode.Trim().ToUpperInvariant();
}
