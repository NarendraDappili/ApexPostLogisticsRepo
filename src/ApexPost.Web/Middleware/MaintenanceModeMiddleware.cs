using Microsoft.Extensions.Options;

namespace ApexPost.Web.Middleware;

public sealed class MaintenanceOptions
{
    public bool Enabled { get; set; }
    public int RetryAfterSeconds { get; set; } = 300;
}

public sealed class MaintenanceModeMiddleware(RequestDelegate next, IOptionsMonitor<MaintenanceOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var current = options.CurrentValue;
        if (current.Enabled && !context.Request.Path.StartsWithSegments("/health"))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = Math.Max(1, current.RetryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new { title = "Service temporarily unavailable.", status = StatusCodes.Status503ServiceUnavailable, correlationId = CorrelationIdMiddleware.Get(context) }, context.RequestAborted).ConfigureAwait(false);
            return;
        }
        await next(context).ConfigureAwait(false);
    }
}
