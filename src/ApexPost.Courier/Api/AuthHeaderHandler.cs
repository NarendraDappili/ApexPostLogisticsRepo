using System.Net.Http.Headers;

namespace ApexPost.Courier.Api;

/// <summary>
/// Adds Authorization: Bearer &lt;token&gt; from the token store to every request of <see cref="ApexApiClient"/>.
/// Without a stored token the request goes without one. The token is read per request, so signing in or out takes
/// effect at once. It is never logged, and never sent over plain HTTP to anything but this machine.
/// </summary>
internal sealed class AuthHeaderHandler(ITokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (!IsSafeForSecrets(request.RequestUri))
            {
                throw new InvalidOperationException(
                    $"Refusing to send the courier's token to {request.RequestUri?.GetLeftPart(UriPartial.Authority)} over plain HTTP. Use an https:// address.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>HTTPS, or plain HTTP to this machine (localhost, 127.0.0.1, ::1), where nothing travels over a network.</summary>
    public static bool IsSafeForSecrets(Uri? uri) => uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
}
