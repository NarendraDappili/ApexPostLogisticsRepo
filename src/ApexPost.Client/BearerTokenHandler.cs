using System.Net.Http.Headers;

namespace ApexPost.Client;

/// <summary>
/// Adds Authorization: Bearer &lt;token&gt; to every request the client sends. The token is asked for on each request,
/// so a token that was renewed or removed takes effect at once. With no token the request goes without one, and the
/// API answers 401. The handler never logs or stores the token.
/// </summary>
public sealed class BearerTokenHandler(Func<CancellationToken, ValueTask<string?>> getToken) : DelegatingHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await getToken(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
