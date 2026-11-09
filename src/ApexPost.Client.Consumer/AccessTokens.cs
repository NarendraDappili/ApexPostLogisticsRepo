using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ApexPost.Client.Consumer;

/// <summary>
/// Where the consumer's bearer token comes from. ApexPostApi:AccessToken, when set (command line, environment
/// variable ApexPostApi__AccessToken), is used as it is: a token from dotnet user-jwts or from an identity provider.
/// Otherwise the consumer asks a Development API's POST /dev/token for a one-hour Courier and Admin token, and asks
/// again shortly before it expires. The token is kept in memory only and never written out.
/// </summary>
public sealed class AccessTokens(IConfiguration configuration, IHttpClientFactory httpClients, TimeProvider clock)
{
    public const string DevelopmentTokenClient = "apexpost-dev-token";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAtUtc;

    public async ValueTask<string?> GetAsync(CancellationToken cancellationToken)
    {
        if (configuration["ApexPostApi:AccessToken"] is { Length: > 0 } configured) return configured;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is null || clock.GetUtcNow() > _expiresAtUtc.AddMinutes(-5))
            {
                using var response = await httpClients.CreateClient(DevelopmentTokenClient).PostAsJsonAsync("dev/token", new
                {
                    name = "ApexPost client consumer",
                    roles = new[] { "Courier", "Admin" },
                    courierId = configuration["ApexPostApi:CourierId"],
                }, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"No access token: ApexPostApi:AccessToken is not set and POST /dev/token answered {(int)response.StatusCode} (it exists only on a Development API). "
                        + "Pass one with --ApexPostApi:AccessToken=<token>; see SECURITY.md.");
                }
                var issued = await response.Content.ReadFromJsonAsync<IssuedToken>(cancellationToken)
                    ?? throw new HttpRequestException("POST /dev/token returned no token.");
                (_token, _expiresAtUtc) = (issued.AccessToken, issued.ExpiresAtUtc);
            }
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
