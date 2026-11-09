using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Api;

/// <summary>
/// Adds Authorization: Bearer &lt;token&gt; from the token store to every request of <see cref="ApexApiClient"/>.
/// The token is read per request, so signing in or out takes effect at once.
/// <list type="bullet">
/// <item>An expired token is not sent at all: it is cleared and the sign-in ends.</item>
/// <item>A 401 for the token that was sent clears it and ends the sign-in, so the app takes the courier back to
/// Login (SECURITY.md). A 401 for an older token, answered after the courier signed in again, changes nothing.</item>
/// <item>The token is never logged, and never sent over plain HTTP to anything but this machine.</item>
/// </list>
/// </summary>
internal sealed class AuthHeaderHandler(ITokenStore tokens, CourierSession session, TimeProvider clock, ILogger<AuthHeaderHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null && tokens.ExpiresAtUtc is { } expiresAt && clock.GetUtcNow() >= expiresAt)
        {
            if (tokens.ClearIfCurrent(token))
            {
                logger.LogWarning("The sign-in expired at {ExpiresAtUtc:u}; the courier must sign in again", expiresAt);
                session.EndSignIn($"Your sign-in expired at {expiresAt.UtcDateTime:HH:mm} UTC. Please sign in again; your scans are saved on this device.");
            }

            token = null;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            if (!IsSafeForSecrets(request.RequestUri))
            {
                throw new InvalidOperationException(
                    $"Refusing to send the courier's token to {request.RequestUri?.GetLeftPart(UriPartial.Authority)} over plain HTTP. Use an https:// address.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null && tokens.ClearIfCurrent(token))
        {
            logger.LogWarning("The server no longer accepts the courier's token (401); it was cleared and the courier must sign in again");
            session.EndSignIn("The server no longer accepts your sign-in. Please sign in again; your scans are saved on this device.");
        }

        return response;
    }

    /// <summary>HTTPS, or plain HTTP to this machine (localhost, 127.0.0.1, ::1), where nothing travels over a network.</summary>
    public static bool IsSafeForSecrets(Uri? uri) => uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
}
