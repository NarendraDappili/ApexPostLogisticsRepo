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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted)
        {
            context.Response.StatusCode = 499;
            logger.LogInformation("Request was aborted by the client {CorrelationId}", CorrelationIdMiddleware.Get(context));
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var correlationId = CorrelationIdMiddleware.Get(context);
            logger.LogError("Unhandled request failure {CorrelationId} ({ExceptionType})", correlationId, exception.GetType().Name);
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
