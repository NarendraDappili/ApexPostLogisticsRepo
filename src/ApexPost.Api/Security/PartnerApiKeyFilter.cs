using System.Net.Mime;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ApexPost.Api.Security;

/// <summary>
/// Marks an action that partners call with an X-Api-Key header instead of a bearer token. It goes together with
/// [AllowAnonymous], which lifts the bearer requirement; this attribute puts the key check in its place, and a test
/// checks that the two are never apart.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequirePartnerApiKeyAttribute : Attribute;

/// <summary>
/// The endpoint filter that checks the partner's API key (SECURITY.md). It runs before the action, so a request
/// without a valid key learns nothing, not even whether the shipment exists. The key is read from the X-Api-Key
/// header only: a query string ends up in logs and browser histories. A missing or invalid key is a 401
/// ProblemDetails. Neither the presented key nor any stored key is ever logged.
/// </summary>
internal static class PartnerApiKeyFilter
{
    public const string HeaderName = "X-Api-Key";

    /// <summary>The scheme in the 401's WWW-Authenticate header; it names the header the key goes in.</summary>
    public const string Challenge = $"ApiKey header=\"{HeaderName}\"";

    public const string UnauthorizedTitle = "A partner API key is required.";

    /// <summary>
    /// Adds the filter to the actions marked <see cref="RequirePartnerApiKeyAttribute"/>. Every other action keeps its
    /// pipeline unchanged: the factory returns it as it is.
    /// </summary>
    public static TBuilder RequirePartnerApiKeysWhereMarked<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilterFactory((context, next) =>
            context.MethodInfo.IsDefined(typeof(RequirePartnerApiKeyAttribute))
                ? invocation => CheckAsync(invocation, next)
                : next);

    private static async ValueTask<object?> CheckAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var httpContext = invocation.HttpContext;
        var services = httpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PartnerApiKeyFilter).FullName!);
        var presented = httpContext.Request.Headers[HeaderName];

        // Two X-Api-Key headers are refused rather than guessed between.
        var partner = presented.Count == 1 ? services.GetRequiredService<PartnerApiKeys>().FindPartner(presented[0]) : null;
        if (partner is not null)
        {
            logger.LogInformation("Partner {Partner} called {Method} {Path}", partner, httpContext.Request.Method, httpContext.Request.Path);
            return await next(invocation);
        }

        var missing = presented.Count == 0 || string.IsNullOrEmpty(presented[0]);
        logger.LogWarning("Partner request refused: {Reason} API key for {Method} {Path}", missing ? "no" : "an invalid", httpContext.Request.Method, httpContext.Request.Path);
        httpContext.Response.Headers.WWWAuthenticate = Challenge;
        var problem = services.GetRequiredService<ProblemDetailsFactory>().CreateProblemDetails(
            httpContext,
            StatusCodes.Status401Unauthorized,
            UnauthorizedTitle,
            detail: missing
                ? $"Send your partner API key in the {HeaderName} header."
                : $"The API key in the {HeaderName} header is not valid. Check it with ApexPost partner support.");
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized, ContentTypes = { MediaTypeNames.Application.ProblemJson } };
    }
}
