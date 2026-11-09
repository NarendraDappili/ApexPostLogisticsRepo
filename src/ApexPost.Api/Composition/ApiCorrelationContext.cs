using System.Diagnostics;
using ApexPost.Application.Abstractions;

namespace ApexPost.Api.Composition;

/// <summary>
/// The current request's W3C trace ID, so the DataChanges rows an API call writes can be found from its logs and
/// its ProblemDetails <c>traceId</c>. None outside a request.
/// </summary>
internal sealed class ApiCorrelationContext(IHttpContextAccessor httpContextAccessor) : ICorrelationContext
{
    public string? CorrelationId => httpContextAccessor.HttpContext is { } context
        ? Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
        : null;
}
