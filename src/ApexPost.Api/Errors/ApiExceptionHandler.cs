using ApexPost.Application.Abstractions;
using ApexPost.Application.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Errors;

/// <summary>
/// The API's global exception handler: every exception an endpoint throws ends here and leaves as ProblemDetails.
/// <list type="bullet">
/// <item>The application's typed errors are the caller's to fix: a validation error is a 400 with the fields that
/// failed, a missing record a 404 and a conflict with the stored state a 409. Their messages are written for the
/// caller, so they are returned in every environment.</item>
/// <item>Anything else is a 500. The decision is by type only, so a .NET or SQL exception is never mistaken for a
/// client error, even an ArgumentException. It is logged with its stack trace; the response only says that an error
/// happened and gives the traceId to quote. Only in Development does the response also carry the exception's type,
/// message and stack trace, for the developer running the API locally.</item>
/// </list>
/// A request the client abandoned never reaches this handler: the exception handler middleware answers it with 499.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment environment, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = ClientProblem(exception);
        if (problem is not null)
        {
            logger.LogWarning("Request rejected with {StatusCode} ({ExceptionType})", problem.Status, exception.GetType().Name);
        }
        else
        {
            logger.LogError(exception, "Unhandled failure in {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected server error occurred.",
                Detail = "The error has been logged. Quote the traceId when you report it.",
            };
            if (environment.IsDevelopment())
            {
                problem.Extensions["exception"] = new DevelopmentExceptionDetails(exception.GetType().FullName ?? exception.GetType().Name, exception.Message, exception.ToString());
            }
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails? ClientProblem(Exception exception) => exception switch
    {
        RequestValidationException validation => ValidationProblem(validation),
        NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested record was not found.", Detail = exception.Message },
        ConflictException => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The request conflicts with the current data.", Detail = exception.Message },
        _ => null,
    };

    // "weightKg: The weight must be ..." becomes errors["weightKg"], the same shape as [ApiController]'s automatic 400.
    private static ValidationProblemDetails ValidationProblem(RequestValidationException exception)
    {
        var errors = exception.Errors
            .Select(error => error.IndexOf(": ", StringComparison.Ordinal) is var colon and > 0 ? (Field: error[..colon], Message: error[(colon + 2)..]) : (Field: string.Empty, Message: error))
            .GroupBy(error => error.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
        return new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest };
    }

    /// <summary>What a developer sees in a 500 when running the API in Development; never sent in any other environment.</summary>
    internal sealed record DevelopmentExceptionDetails(string Type, string Message, string StackTrace);
}
