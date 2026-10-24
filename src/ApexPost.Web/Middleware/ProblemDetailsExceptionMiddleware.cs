using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Middleware;

public sealed class ProblemDetailsExceptionMiddleware(RequestDelegate next, ILogger<ProblemDetailsExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var correlationId = CorrelationIdMiddleware.Get(context);
            logger.LogError(exception, "Unhandled request failure {CorrelationId}", correlationId);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected server error occurred.",
                Type = "https://httpstatuses.com/500"
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;
            problem.Extensions["correlationId"] = correlationId;
            await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
    }
}
