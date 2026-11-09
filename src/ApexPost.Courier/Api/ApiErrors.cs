using System.Net;

namespace ApexPost.Courier.Api;

/// <summary>
/// The server answered, but not with what the app asked for: an error status (with its ProblemDetails, if the body
/// was one), or a success whose body could not be read. <see cref="Exception.Message"/> is written for the courier;
/// <see cref="StatusCode"/> and <see cref="Problem"/> keep the technical details.
/// </summary>
internal sealed class ApexApiException : Exception
{
    public ApexApiException(HttpStatusCode statusCode, ApiProblem? problem, Exception? innerException = null)
        : this(statusCode, problem, FriendlyMessage(statusCode, problem), innerException)
    {
    }

    private ApexApiException(HttpStatusCode statusCode, ApiProblem? problem, string message, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Problem = problem;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>The error body, when the server sent ProblemDetails; null for an empty or non-JSON body.</summary>
    public ApiProblem? Problem { get; }

    /// <summary>A success status whose body was missing, not JSON, or not the expected shape.</summary>
    public static ApexApiException Unreadable(HttpStatusCode statusCode, Exception? innerException = null) =>
        new(statusCode, null, "The server's answer could not be read. Nothing was changed on this device; try again later.", innerException);

    /// <summary>
    /// What the courier is told. It uses the server's own explanation where that is meant for the caller (the
    /// ProblemDetails detail, the validation errors), and never shows server internals: a 5xx gives only a
    /// reference to quote.
    /// </summary>
    internal static string FriendlyMessage(HttpStatusCode statusCode, ApiProblem? problem)
    {
        var detail = string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem.Detail.Trim();
        var status = (int)statusCode;
        return status switch
        {
            400 when problem?.Errors is { Count: > 0 } errors => $"The server did not accept the data: {Summarise(errors)}",
            400 => $"The server did not accept the data. {detail ?? problem?.Title ?? "Check the scan and try again."}",
            401 => $"Please sign in again. {detail ?? "The server did not accept your sign-in."}",
            403 => $"Not allowed. {detail ?? "Your account may not do this."}",
            404 => detail ?? "Not found on the server.",
            408 => "The server did not get the whole request in time. Your scans are saved on this device and will be sent on the next sync.",
            409 => detail ?? "The server refused because of a conflict. Your scans are saved on this device and will be sent on the next sync.",
            >= 500 => "The server had a problem" + (string.IsNullOrWhiteSpace(problem?.TraceId) ? "" : $" (reference {problem.TraceId})")
                + ". Your scans are saved on this device and will be sent on the next sync.",
            _ => detail ?? problem?.Title ?? $"The server answered {status}.",
        };
    }

    private static string Summarise(Dictionary<string, string[]?> errors)
    {
        const int shown = 3;
        var lines = errors
            .OrderBy(error => error.Key, StringComparer.Ordinal)
            .Select(error => $"{(error.Key.Length == 0 ? "request" : error.Key)}: {string.Join(" ", (error.Value ?? []).Where(message => !string.IsNullOrWhiteSpace(message)))}".TrimEnd(' ', ':'))
            .ToList();
        var more = lines.Count > shown ? $" (and {lines.Count - shown} more)" : "";
        return string.Join("; ", lines.Take(shown)) + more;
    }
}

/// <summary>Why the server could not be reached at all.</summary>
internal enum UnavailableReason
{
    /// <summary>No connection: no network, DNS failure, connection refused or reset.</summary>
    NoConnection,

    /// <summary>The server did not answer within the timeouts (RESILIENCE.md).</summary>
    TimedOut,

    /// <summary>The circuit breaker is open after repeated failures; the call was not even tried.</summary>
    CircuitOpen,
}

/// <summary>
/// The server gave no answer. Nothing is lost: the scans stay Pending on the device. <see cref="Exception.Message"/>
/// is written for the courier.
/// </summary>
internal sealed class ApiUnavailableException(UnavailableReason reason, Exception innerException)
    : Exception(MessageFor(reason), innerException)
{
    public UnavailableReason Reason { get; } = reason;

    private static string MessageFor(UnavailableReason reason) => reason switch
    {
        UnavailableReason.CircuitOpen => $"{ServerAvailability.Banner}. Your scans are saved on this device and will be sent when the server answers again.",
        UnavailableReason.TimedOut => "The server did not answer in time. Your scans are saved on this device and will be sent on the next sync.",
        _ => "The server cannot be reached: check the network. Your scans are saved on this device and will be sent on the next sync.",
    };
}
