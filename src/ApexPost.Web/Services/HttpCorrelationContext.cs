using ApexPost.Application.Abstractions;
using ApexPost.Web.Middleware;

namespace ApexPost.Web.Services;

/// <summary>The current request's correlation ID (see <see cref="CorrelationIdMiddleware"/>); none outside a request.</summary>
public sealed class HttpCorrelationContext(IHttpContextAccessor httpContextAccessor) : ICorrelationContext
{
    public string? CorrelationId => httpContextAccessor.HttpContext is { } context ? CorrelationIdMiddleware.Get(context) : null;
}
