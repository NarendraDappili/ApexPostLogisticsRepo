using System.Diagnostics;
using System.Text.RegularExpressions;
using ApexPost.Application.Abstractions;
using ApexPost.Domain.Entities;

namespace ApexPost.Web.Middleware;

/// <summary>A client error the caller can act on, with a message safe to show.</summary>
public sealed record ProblemMapping(int Status, string Title, string Detail);

/// <summary>
/// Decides which unhandled exceptions are the client's problem. Conflicts are 409, missing records 404, and broken
/// business rules 400. Everything else, including the same exception types thrown by framework or library code,
/// stays a 500 whose details are never returned.
/// </summary>
public static partial class ExceptionProblemMapping
{
    private static readonly System.Reflection.Assembly CoreLibrary = typeof(object).Assembly;
    private static readonly System.Reflection.Assembly[] BusinessAssemblies = [typeof(Branch).Assembly, typeof(IUnitOfWork).Assembly];

    public static ProblemMapping? Map(Exception exception) => exception switch
    {
        ConflictException => new(StatusCodes.Status409Conflict, "The request conflicts with the current data.", exception.Message),
        NotFoundException => new(StatusCodes.Status404NotFound, "The requested record was not found.", exception.Message),
        RuleViolationException => new(StatusCodes.Status400BadRequest, "The request breaks a business rule.", exception.Message),
        ArgumentException or InvalidOperationException when IsThrownByBusinessCode(exception) =>
            new(StatusCodes.Status400BadRequest, "The request breaks a business rule.", WithoutParameterName(exception.Message)),
        _ => null,
    };

    /// <summary>
    /// True when the first frame outside the .NET core library belongs to ApexPost.Domain or ApexPost.Application,
    /// so guard helpers such as ArgumentException.ThrowIfNullOrWhiteSpace count as the business code that called them.
    /// </summary>
    private static bool IsThrownByBusinessCode(Exception exception)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames())
        {
            var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
            if (assembly is null || assembly == CoreLibrary) continue;
            return BusinessAssemblies.Contains(assembly);
        }

        return false;
    }

    // "Quantity must be positive. (Parameter 'quantity')" -> "Quantity must be positive."
    private static string WithoutParameterName(string message) => ParameterSuffix().Replace(message, string.Empty);

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)(\r?\nActual value was .*)?$", RegexOptions.Singleline)]
    private static partial Regex ParameterSuffix();
}
