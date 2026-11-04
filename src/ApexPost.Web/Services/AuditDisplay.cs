using System.Globalization;
using ApexPost.Application.Auditing;

namespace ApexPost.Web.Services;

/// <summary>
/// How the audit log screen shows its values, with the theme's tinted badges: green for what went through, yellow for
/// a change or a warning, red for a failure or a refusal, slate for neutral events.
/// </summary>
public static class AuditDisplay
{
    public static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string StatusBadge(int statusCode) => statusCode switch
    {
        < 400 => "text-bg-success",
        < 500 => "text-bg-warning",
        _ => "text-bg-danger",
    };

    public static string OutcomeBadge(string outcome) => outcome == "Succeeded" ? "text-bg-success" : "text-bg-danger";

    public static string OperationBadge(DataChangeOperation operation) => operation switch
    {
        DataChangeOperation.Insert => "text-bg-success",
        DataChangeOperation.Update => "text-bg-warning",
        _ => "text-bg-danger",
    };

    public static string EventBadge(SecurityEventType eventType) => eventType switch
    {
        SecurityEventType.SignInSucceeded => "text-bg-success",
        SecurityEventType.SignedOut => "text-bg-secondary",
        SecurityEventType.SignInFailed or SecurityEventType.RateLimited or SecurityEventType.SessionRejected => "text-bg-warning",
        _ => "text-bg-danger",
    };

    /// <summary>The start of a correlation ID, enough to tell requests apart in a list; the full ID is in the link title.</summary>
    public static string ShortId(string? correlationId) =>
        string.IsNullOrEmpty(correlationId) ? string.Empty : correlationId.Length <= 10 ? correlationId : correlationId[..8] + "…";
}
