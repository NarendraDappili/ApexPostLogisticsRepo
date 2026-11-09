namespace ApexPost.Courier.Api;

/// <summary>
/// Where the app keeps the courier's access token between requests. It plays the part of MAUI's SecureStorage: the
/// only place the token lives (SECURITY.md).
/// </summary>
internal interface ITokenStore
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

/// <summary>
/// Keeps the token in this process's memory only: never in a file, a setting or a log, so it is gone when the app
/// closes and the courier signs in again. A console program has no secure store of its own the way a phone has.
/// </summary>
internal sealed class InMemoryTokenStore : ITokenStore
{
    private StoredToken? _current;

    public DateTimeOffset? ExpiresAtUtc => Volatile.Read(ref _current)?.ExpiresAtUtc;

    public ValueTask<string?> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Volatile.Read(ref _current)?.Value);

    public ValueTask SaveAsync(string token, DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Volatile.Write(ref _current, new StoredToken(token, expiresAtUtc));
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _current, null);
        return ValueTask.CompletedTask;
    }

    public bool ClearIfCurrent(string token)
    {
        var current = Volatile.Read(ref _current);
        return current is not null && current.Value == token && Interlocked.CompareExchange(ref _current, null, current) == current;
    }

    /// <summary>The token and its expiry, replaced together so a reader never sees one without the other.</summary>
    private sealed class StoredToken(string value, DateTimeOffset? expiresAtUtc)
    {
        public string Value { get; } = value;

        public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;

        // Never the token itself, should this object ever reach a log or a debugger display.
        public override string ToString() => "StoredToken(****)";
    }
}
