using System.Diagnostics;

namespace ApexPost.Web.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var watch = Stopwatch.StartNew();
        var correlationId = CorrelationIdMiddleware.Get(context);
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            watch.Stop();
            logger.LogInformation("HTTP {Method} {Path} completed {StatusCode} in {ElapsedMilliseconds}ms {CorrelationId}", context.Request.Method, context.Request.Path, context.Response.StatusCode, watch.Elapsed.TotalMilliseconds, correlationId);
        }
    }
}
