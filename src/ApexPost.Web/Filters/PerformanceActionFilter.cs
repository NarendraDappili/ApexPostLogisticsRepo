using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ApexPost.Web.Filters;

/// <summary>Warns when MVC action execution, including its filters, exceeds the configured budget.</summary>
public sealed class PerformanceActionFilter(IOptions<ActionPerformanceOptions> options, ILogger<PerformanceActionFilter> logger) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => 1000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            var threshold = Math.Max(0, options.Value.WarningThresholdMilliseconds);
            if (stopwatch.ElapsedMilliseconds >= threshold)
            {
                logger.LogWarning("MVC action {Controller}/{Action} took {ElapsedMilliseconds} ms, exceeding the {ThresholdMilliseconds} ms threshold.",
                    context.RouteData.Values["controller"], context.RouteData.Values["action"], stopwatch.ElapsedMilliseconds, threshold);
            }
        }
    }
}
