using System.Net;
using System.Security.Cryptography;
using System.Text;
using ApexPost.Application.Auditing;
using ApexPost.Web.Security;
using Microsoft.Extensions.Options;

namespace ApexPost.Web.Middleware;

public sealed class IntegrationSecurityOptions
{
    public string? ApiKey { get; set; }
    public string[] AllowedIps { get; set; } = [];
}

/// <summary>
/// Works out, once per request, which partner the request's <c>X-Api-Key</c> proves. The rate limiter partitions by
/// this verified identity, never by a header the caller chooses (such as <c>X-Partner-Id</c>), and the API key
/// middleware uses the same answer. The identity is a fingerprint of the configured key, so neither the key nor the
/// supplied value is ever kept or logged.
/// </summary>
public static class IntegrationPartner
{
    private static readonly object ItemKey = new();

    /// <summary>The verified partner's identity, or <see langword="null"/> for a missing, wrong or unconfigured key.</summary>
    public static string? Resolve(HttpContext context, IntegrationSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        if (context.Items.TryGetValue(ItemKey, out var cached)) return (string?)cached;
        var configuredKey = options.ApiKey;
        var suppliedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        string? partner = null;
        if (!string.IsNullOrWhiteSpace(configuredKey) && !string.IsNullOrEmpty(suppliedKey) && FixedTimeEquals(configuredKey, suppliedKey))
        {
            partner = "partner-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)))[..12];
        }
        context.Items[ItemKey] = partner;
        return partner;
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}

public sealed class IntegrationApiKeyMiddleware(RequestDelegate next, IOptionsMonitor<IntegrationSecurityOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequireIntegrationApiKeyAttribute>() is not null)
        {
            var configuredKey = options.CurrentValue.ApiKey;
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { title = "Integration authentication is not configured.", status = StatusCodes.Status500InternalServerError, correlationId = CorrelationIdMiddleware.Get(context) }, context.RequestAborted).ConfigureAwait(false);
                return;
            }
            var suppliedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (IntegrationPartner.Resolve(context, options.CurrentValue) is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                // Only whether a key was sent is recorded, never the key itself.
                await SecurityEventRecorder.RecordAsync(context, SecurityEventType.AccessDenied, StatusCodes.Status401Unauthorized,
                    string.IsNullOrEmpty(suppliedKey) ? "Missing partner API key" : "Invalid partner API key").ConfigureAwait(false);
                await context.Response.WriteAsJsonAsync(new { title = "Integration API key is invalid.", status = StatusCodes.Status401Unauthorized, correlationId = CorrelationIdMiddleware.Get(context) }, context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        await next(context).ConfigureAwait(false);
    }
}

public sealed class IpAllowlistMiddleware(RequestDelegate next, IOptionsMonitor<IntegrationSecurityOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequireIpAllowlistAttribute>() is not null)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            var allowed = options.CurrentValue.AllowedIps.Any(entry => IPAddress.TryParse(entry, out var configured) && AddressesEqual(configured, remoteIp));
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await SecurityEventRecorder.RecordAsync(context, SecurityEventType.AccessDenied, StatusCodes.Status403Forbidden, "Client IP is not in the allow-list").ConfigureAwait(false);
                await context.Response.WriteAsJsonAsync(new { title = "Client IP is not allowed.", status = StatusCodes.Status403Forbidden, correlationId = CorrelationIdMiddleware.Get(context) }, context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        await next(context).ConfigureAwait(false);
    }

    private static bool AddressesEqual(IPAddress configured, IPAddress? remote) => remote is not null && configured.Equals(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
}
