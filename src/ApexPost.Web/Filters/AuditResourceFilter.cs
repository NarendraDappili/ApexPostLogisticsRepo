using System.Diagnostics;
using System.Security.Claims;
using ApexPost.Application.Auditing;
using ApexPost.Web.Middleware;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApexPost.Web.Filters;

/// <summary>
/// Audits every MVC request with safe metadata only: no request bodies, action arguments or unlisted route values.
/// It is a resource filter, so it wraps model binding, the action and the result execution. The status it records
/// is the one the client receives: the response status once the result has run (redirects, Forbid, Challenge,
/// views, Problem Details), or, when an exception escapes MVC, the status ProblemDetailsExceptionMiddleware will send
/// for it (409/404/400 for mapped business errors, 499 for a client abort, otherwise 500).
/// </summary>
public sealed class AuditResourceFilter(IAuditStore auditStore, ILogger<AuditResourceFilter> logger) : IAsyncResourceFilter, IOrderedFilter
{
    private const int ClientClosedRequest = 499;

    private static readonly HashSet<string> SafeRouteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "branchCode", "counterCode", "transactionId", "shipmentId", "moneyOrderId", "productId"
    };

    public int Order => -2000;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        ResourceExecutedContext? executed = null;
        Exception? thrown = null;
        try
        {
            executed = await next().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            thrown = exception;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var httpContext = context.HttpContext;
            var escaped = thrown ?? (executed is { Exception: { } unhandled, ExceptionHandled: false } ? unhandled : null);
            var statusCode = escaped is null ? httpContext.Response.StatusCode : StatusFor(escaped, httpContext);
            var record = new AuditRecord(
                DateTimeOffset.UtcNow,
                httpContext.User.FindFirst(ApexPostClaimTypes.StaffId)?.Value,
                httpContext.User.FindFirst(ClaimTypes.Role)?.Value,
                httpContext.User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value,
                context.RouteData.Values["controller"]?.ToString() ?? "Unknown",
                context.RouteData.Values["action"]?.ToString() ?? "Unknown",
                GetSafeRouteValues(context),
                statusCode,
                stopwatch.ElapsedMilliseconds,
                CorrelationIdMiddleware.Get(httpContext),
                !context.ModelState.IsValid || statusCode is < 200 or >= 400 ? "Failed" : "Succeeded");
            try
            {
                // The store saves with CancellationToken.None: a request the client abandoned is still audited.
                await auditStore.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) { logger.LogError(exception, "Unable to append MVC audit record for {Controller}/{Action}.", record.Controller, record.Action); }
        }
    }

    // Mirrors ProblemDetailsExceptionMiddleware, which turns the escaped exception into the response.
    private static int StatusFor(Exception exception, HttpContext httpContext) =>
        exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested
            ? ClientClosedRequest
            : ExceptionProblemMapping.Map(exception)?.Status ?? StatusCodes.Status500InternalServerError;

    private static IReadOnlyDictionary<string, string> GetSafeRouteValues(FilterContext context) =>
        context.RouteData.Values
            .Where(pair => SafeRouteKeys.Contains(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => Limit(pair.Value!.ToString()), StringComparer.OrdinalIgnoreCase);

    private static string Limit(string? value) => string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, 128)];
}
