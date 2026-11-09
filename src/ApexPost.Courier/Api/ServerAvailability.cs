namespace ApexPost.Courier.Api;

/// <summary>The API circuit breaker's state, as the screens show it.</summary>
internal enum ConnectionState
{
    /// <summary>Normal: calls go to the server.</summary>
    Closed,

    /// <summary>The server failed repeatedly: calls fail at once, and the app works offline.</summary>
    Open,

    /// <summary>The break is over: the next call is a trial, and its outcome closes or reopens the circuit.</summary>
    HalfOpen,
}

/// <summary>
/// What the app knows about reaching the server, kept up to date by the circuit breaker's callbacks
/// (<see cref="ApiResilience"/>). While the circuit is not closed the screens show <see cref="Banner"/>; scans are
/// still saved on the device as usual.
/// </summary>
internal sealed class ServerAvailability(TimeProvider clock)
{
    public const string Banner = "Server unavailable – working offline";

    private readonly Lock _gate = new();

    public ConnectionState State { get; private set; } = ConnectionState.Closed;

    /// <summary>When the circuit last opened; null if it has not opened in this session.</summary>
    public DateTimeOffset? OpenedAtUtc { get; private set; }

    /// <summary>When an open circuit lets the next trial call through.</summary>
    public DateTimeOffset? RetryAfterUtc { get; private set; }

    public bool IsWorkingOffline => State != ConnectionState.Closed;

    internal void Opened(TimeSpan breakDuration)
    {
        lock (_gate)
        {
            State = ConnectionState.Open;
            OpenedAtUtc = clock.GetUtcNow();
            RetryAfterUtc = OpenedAtUtc + breakDuration;
        }
    }

    internal void HalfOpened()
    {
        lock (_gate) State = ConnectionState.HalfOpen;
    }

    internal void Closed()
    {
        lock (_gate)
        {
            State = ConnectionState.Closed;
            RetryAfterUtc = null;
        }
    }
}
