namespace ApexPost.Courier.Api;

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
