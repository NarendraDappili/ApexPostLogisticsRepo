using System.Diagnostics;
using System.Security.Claims;
using ApexPost.Application.Auditing;
using ApexPost.Web.Middleware;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ApexPost.Web.Filters;

/// <summary>Captures safe MVC action metadata without serializing request bodies or action arguments.</summary>
public sealed class AuditActionFilter(IAuditStore auditStore, ILogger<AuditActionFilter> logger) : IAsyncActionFilter, IOrderedFilter
{
    private static readonly HashSet<string> SafeRouteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "branchCode", "counterCode", "transactionId", "shipmentId", "moneyOrderId", "productId"
    };

    public int Order => -2000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        ActionExecutedContext? executed = null;
        try
        {
            executed = await next().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            var httpContext = context.HttpContext;
            var result = executed?.Result ?? context.Result;
            var statusCode = GetStatusCode(result, executed?.Exception is not null, httpContext.Response.StatusCode);
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
                await auditStore.AppendAsync(record, httpContext.RequestAborted).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Unable to append MVC audit record for {Controller}/{Action}.", record.Controller, record.Action); }
        }
    }

    private static IReadOnlyDictionary<string, string> GetSafeRouteValues(ActionExecutingContext context) =>
        context.RouteData.Values
            .Where(pair => SafeRouteKeys.Contains(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => Limit(pair.Value!.ToString()), StringComparer.OrdinalIgnoreCase);

    private static int GetStatusCode(IActionResult? result, bool hasException, int responseStatusCode) =>
        hasException ? StatusCodes.Status500InternalServerError : result switch
        {
            IStatusCodeActionResult { StatusCode: int statusCode } => statusCode,
            RedirectResult => StatusCodes.Status302Found,
            RedirectToActionResult => StatusCodes.Status302Found,
            RedirectToRouteResult => StatusCodes.Status302Found,
            _ when responseStatusCode >= StatusCodes.Status400BadRequest => responseStatusCode,
            _ => StatusCodes.Status200OK
        };

    private static string Limit(string? value) => string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, 128)];
}
