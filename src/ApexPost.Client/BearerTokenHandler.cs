using System.Net.Http.Headers;

namespace ApexPost.Client;

/// <summary>
/// Adds Authorization: Bearer &lt;token&gt; to every request the client sends. The token is asked for on each request,
/// so a token that was renewed or removed takes effect at once. With no token the request goes without one, and the
/// API answers 401. The handler never logs or stores the token.
/// It refuses to send a token over plain HTTP to anything but this machine: whoever can see the traffic could copy the
/// token and use it until it expires (SECURITY.md). Local development over http://localhost stays possible.
/// </summary>
public sealed class BearerTokenHandler(Func<CancellationToken, ValueTask<string?>> getToken) : DelegatingHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsSafeForSecrets(request.RequestUri))
        {
            throw new InvalidOperationException(
                $"Refusing to send a bearer token to {request.RequestUri?.GetLeftPart(UriPartial.Authority)} over plain HTTP. Use an https:// base address.");
        }

        var token = await getToken(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>HTTPS, or plain HTTP to this machine (localhost, 127.0.0.1, ::1), where nothing travels over a network.</summary>
    public static bool IsSafeForSecrets(Uri? uri) => uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
}
