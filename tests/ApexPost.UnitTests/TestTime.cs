namespace ApexPost.UnitTests;

/// <summary>
/// The fixed "now" of the unit tests. No test reads the system clock (TESTING-STRATEGY.md §8): code that needs the time
/// takes it as an argument or from a <c>FakeTimeProvider</c> started at this instant.
/// </summary>
internal static class TestTime
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
}
