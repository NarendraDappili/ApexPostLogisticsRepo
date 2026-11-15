namespace ApexPost.Courier.IntegrationTests;

/// <summary>
/// Fixed instants for the courier tests (TESTING-STRATEGY.md §8). The app under test runs its real composition, whose
/// clock is the system clock: its retry pipeline and circuit breaker wait in real time. So the tests never compute a
/// time from "now". They use instants so far from any real now that no outcome depends on when the tests run.
/// </summary>
internal static class FixedTimes
{
    /// <summary>A fixed "now" for code that takes the time as an argument, and before any real run: a scan dated near it is in the past.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    /// <summary>In the future for any run: a scan the server refuses, a token that never expires during a test.</summary>
    public static readonly DateTimeOffset FarFuture = new(2100, 1, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Expired for any run.</summary>
    public static readonly DateTimeOffset LongExpired = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
