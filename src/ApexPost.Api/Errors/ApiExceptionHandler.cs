using ApexPost.Application.Abstractions;
using ApexPost.Application.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Errors;

/// <summary>
/// Turns the application's typed errors into ProblemDetails: a validation error is a 400 with the fields that failed,
/// a missing record a 404 and a conflict with the stored state a 409. The decision is by type only; the messages of
/// these types are written for the caller. Any other exception is not handled here and becomes a 500.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            RequestValidationException validation => ValidationProblem(validation),
            NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "The requested record was not found.", Detail = exception.Message },
            ConflictException => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "The request conflicts with the current data.", Detail = exception.Message },
            _ => null,
        };
        if (problem is null) return false;

        logger.LogWarning("Request rejected with {StatusCode} ({ExceptionType})", problem.Status, exception.GetType().Name);
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    // "weightKg: The weight must be ..." becomes errors["weightKg"], the same shape as [ApiController]'s automatic 400.
    private static ValidationProblemDetails ValidationProblem(RequestValidationException exception)
    {
        var errors = exception.Errors
            .Select(error => error.IndexOf(": ", StringComparison.Ordinal) is var colon and > 0 ? (Field: error[..colon], Message: error[(colon + 2)..]) : (Field: string.Empty, Message: error))
            .GroupBy(error => error.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
        return new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest };
    }
}
