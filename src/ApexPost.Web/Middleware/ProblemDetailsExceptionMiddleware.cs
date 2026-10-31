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
            var mapping = ExceptionProblemMapping.Map(exception);
            if (mapping is null) logger.LogError("Unhandled request failure {CorrelationId} ({ExceptionType})", correlationId, exception.GetType().Name);
            else logger.LogWarning("Request rejected with {StatusCode} {CorrelationId} ({ExceptionType})", mapping.Status, correlationId, exception.GetType().Name);

            var status = mapping?.Status ?? StatusCodes.Status500InternalServerError;
            context.Response.Clear();
            context.Response.StatusCode = status;
            var problem = new ProblemDetails
            {
                Status = status,
                Title = mapping?.Title ?? "An unexpected server error occurred.",
                // Only business messages written for the user are returned; a 500 never carries exception text.
                Detail = mapping?.Detail,
                Type = $"https://httpstatuses.com/{status}"
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;
            problem.Extensions["correlationId"] = correlationId;
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
    }
}
