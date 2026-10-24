using System.Net;
using System.Security.Cryptography;
using System.Text;
using ApexPost.Web.Security;
using Microsoft.Extensions.Options;

namespace ApexPost.Web.Middleware;

public sealed class IntegrationSecurityOptions
{
    public string? ApiKey { get; set; }
    public string[] AllowedIps { get; set; } = [];
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
            if (string.IsNullOrEmpty(suppliedKey) || !FixedTimeEquals(configuredKey, suppliedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { title = "Integration API key is invalid.", status = StatusCodes.Status401Unauthorized, correlationId = CorrelationIdMiddleware.Get(context) }, context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        await next(context).ConfigureAwait(false);
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
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
                await context.Response.WriteAsJsonAsync(new { title = "Client IP is not allowed.", status = StatusCodes.Status403Forbidden, correlationId = CorrelationIdMiddleware.Get(context) }, context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        await next(context).ConfigureAwait(false);
    }

    private static bool AddressesEqual(IPAddress configured, IPAddress? remote) => remote is not null && configured.Equals(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
}
