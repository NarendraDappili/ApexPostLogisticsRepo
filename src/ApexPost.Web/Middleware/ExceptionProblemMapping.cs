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
    private static readonly System.Reflection.Assembly[] BusinessAssemblies = [typeof(Branch).Assembly, typeof(IUnitOfWork).Assembly];

    // The guard helpers business code calls: ArgumentException.ThrowIfNullOrWhiteSpace, ArgumentNullException.ThrowIfNull,
    // ArgumentOutOfRangeException.ThrowIfNegative and the like. Their frames are skipped; any other .NET frame is not.
    private static readonly Type[] GuardHelperTypes = [typeof(ArgumentException), typeof(ArgumentNullException), typeof(ArgumentOutOfRangeException)];

    public static ProblemMapping? Map(Exception exception) => exception switch
    {
        ConflictException => new(StatusCodes.Status409Conflict, "The request conflicts with the current data.", exception.Message),
        NotFoundException => new(StatusCodes.Status404NotFound, "The requested record was not found.", exception.Message),
        RuleViolationException => new(StatusCodes.Status400BadRequest, "The request breaks a business rule.", exception.Message),
        ArgumentException or InvalidOperationException when IsBusinessError(exception) =>
            new(StatusCodes.Status400BadRequest, "The request breaks a business rule.", WithoutParameterName(exception.Message)),
        _ => null,
    };

    /// <summary>
    /// A type Domain or Application declares (RequestValidationException, IdentityVerificationRequiredException, ...)
    /// is a business error wherever it was thrown. The .NET types the domain uses for its rules need the stack trace.
    /// </summary>
    internal static bool IsBusinessError(Exception exception) =>
        BusinessAssemblies.Contains(exception.GetType().Assembly) || IsThrownByBusinessCode(exception);

    /// <summary>
    /// True when the exception was thrown by ApexPost.Domain or ApexPost.Application code itself, directly or through a
    /// guard helper such as ArgumentException.ThrowIfNullOrWhiteSpace. A bug inside a .NET method that business code
    /// called (a List index out of range, a modified collection, Nullable.Value without a value) is not a broken
    /// rule: the throwing frame is List, ThrowHelper or Nullable, so it stays a 500 and its message is never shown.
    /// </summary>
    internal static bool IsThrownByBusinessCode(Exception exception) => IsThrownBy(exception, BusinessAssemblies);

    internal static bool IsThrownBy(Exception exception, IReadOnlyCollection<System.Reflection.Assembly> assemblies)
    {
        foreach (var frame in new StackTrace(exception, fNeedFileInfo: false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            if (type is null) continue;
            if (GuardHelperTypes.Contains(type)) continue;
            return assemblies.Contains(type.Assembly) || IsController(type);
        }

        return false;
    }

    // Optimized code inlines small business methods (a value object's constructor, the guard inside it) into their
    // caller, so the business frame disappears. Controllers are where the web layer calls business code directly, and
    // they never throw these exceptions themselves (a test enforces it), so such an exception surfacing in a
    // controller's frame, its async state machine or a lambda in it, came from business code inlined there.
    private static bool IsController(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(current)) return true;
        }

        return false;
    }

    // "Quantity must be positive. (Parameter 'quantity')" -> "Quantity must be positive."
    private static string WithoutParameterName(string message) => ParameterSuffix().Replace(message, string.Empty);

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)(\r?\nActual value was .*)?$", RegexOptions.Singleline)]
    private static partial Regex ParameterSuffix();
}
