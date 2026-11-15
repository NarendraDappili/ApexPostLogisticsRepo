namespace ApexPost.Courier.Api;

/// <summary>
/// Where the app keeps the courier's access token between requests: the only place the token lives (SECURITY.md).
/// Each app head supplies its own store. The console app keeps the token in memory (<c>InMemoryTokenStore</c>); a
/// MAUI app would use SecureStorage.
/// </summary>
public interface ITokenStore
{
    ValueTask<string?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>When the stored token stops being accepted; null if there is none or its expiry is unknown.</summary>
    DateTimeOffset? ExpiresAtUtc { get; }

    ValueTask SaveAsync(string token, DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the token only if it is still <paramref name="token"/>: an answer to an old request must not throw
    /// away a token the courier got meanwhile. Returns whether it cleared it.
    /// </summary>
    bool ClearIfCurrent(string token);
}
