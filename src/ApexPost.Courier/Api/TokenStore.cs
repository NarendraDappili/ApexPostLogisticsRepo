namespace ApexPost.Courier.Api;

/// <summary>
/// Where the app keeps the courier's access token between requests. It plays the part of MAUI's SecureStorage: the
/// only place the token lives (SECURITY.md).
/// </summary>
internal interface ITokenStore
{
    ValueTask<string?> GetAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(string token, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the token in this process's memory only: never in a file, a setting or a log, so it is gone when the app
/// closes and the courier signs in again. A console program has no secure store of its own the way a phone has.
/// </summary>
internal sealed class InMemoryTokenStore : ITokenStore
{
    private volatile string? _token;

    public ValueTask<string?> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_token);

    public ValueTask SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _token = null;
        return ValueTask.CompletedTask;
    }
}
