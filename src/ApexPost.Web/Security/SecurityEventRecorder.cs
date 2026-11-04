using System.Security.Claims;
using ApexPost.Application.Auditing;
using ApexPost.Web.Middleware;

namespace ApexPost.Web.Security;

/// <summary>
/// Records sign-ins, sign-outs and refused requests in the SecurityEvents table. Only safe metadata is kept: the
/// method, the path without its query string, the status, the client IP and a short reason; never a password, a
/// request body or a header. Like the request audit, a failed write is logged and never changes the response.
/// </summary>
public sealed class SecurityEventRecorder(ISecurityEventStore store, TimeProvider timeProvider, ILogger<SecurityEventRecorder> logger)
{
    public static Task RecordAsync(HttpContext context, SecurityEventType eventType, int statusCode, string? detail = null, ClaimsPrincipal? principal = null, string? attemptedStaffId = null) =>
        context.RequestServices.GetRequiredService<SecurityEventRecorder>().RecordEventAsync(context, eventType, statusCode, detail, principal, attemptedStaffId);

    public async Task RecordEventAsync(HttpContext context, SecurityEventType eventType, int statusCode, string? detail = null, ClaimsPrincipal? principal = null, string? attemptedStaffId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = principal ?? context.User;
        var record = new SecurityEventRecord(
            timeProvider.GetUtcNow(),
            eventType,
            attemptedStaffId ?? user.FindFirst(ApexPostClaimTypes.StaffId)?.Value,
            user.FindFirst(ClaimTypes.Role)?.Value,
            user.FindFirst(ApexPostClaimTypes.BranchCode)?.Value,
            context.Request.Method,
            context.Request.Path.HasValue ? context.Request.Path.Value! : "/",
            statusCode,
            context.Connection.RemoteIpAddress?.ToString(),
            detail,
            CorrelationIdMiddleware.Get(context));
        try
        {
            // The store saves with CancellationToken.None: a refused client that disconnects is still recorded.
            await store.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) { logger.LogError(exception, "Unable to append security event {EventType} for {Path}.", eventType, record.Path); }
    }
}
