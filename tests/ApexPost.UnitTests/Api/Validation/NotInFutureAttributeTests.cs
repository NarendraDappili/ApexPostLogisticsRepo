using System.ComponentModel.DataAnnotations;
using ApexPost.Api.Validation;

namespace ApexPost.UnitTests.Api.Validation;

/// <summary>
/// Week 5 (S5), risk R7: a scan time may be at most five minutes ahead of the server clock. CustomRuleTests checks the
/// rule at whole minutes (5 accepted, 6 refused); these rows check the exact limit, to the millisecond, with "now"
/// fixed by a <see cref="FakeTimeProvider"/> as the request's services would give it.
/// </summary>
public sealed class NotInFutureAttributeTests
{
    [Theory]
    [InlineData(-86_400_000)]   // a day ago
    [InlineData(0)]             // now
    [InlineData(299_999)]       // just inside the limit
    [InlineData(300_000)]       // exactly five minutes ahead: still accepted
    public void GetValidationResult_UpToFiveMinutesAhead_IsAccepted(int millisecondsAhead)
    {
        // Arrange
        var scannedAt = TestTime.Now.AddMilliseconds(millisecondsAhead);

        // Act
        var result = new NotInFutureAttribute().GetValidationResult(scannedAt, Context());

        // Assert
        Assert.Equal(ValidationResult.Success, result);
    }

    [Theory]
    [InlineData(300_001)]       // one millisecond past the limit
    [InlineData(360_000)]       // six minutes ahead
    public void GetValidationResult_MoreThanFiveMinutesAhead_IsRefusedNamingTheFieldAndTheLimit(int millisecondsAhead)
    {
        // Arrange
        var scannedAt = TestTime.Now.AddMilliseconds(millisecondsAhead);

        // Act
        var result = new NotInFutureAttribute().GetValidationResult(scannedAt, Context());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(["ScannedAtUtc"], result.MemberNames);
        Assert.Equal("scannedAtUtc cannot be in the future; it is more than 5 minutes ahead of the server clock.", result.ErrorMessage);
    }

    [Fact]
    public void GetValidationResult_ClockMovesOnPastTheScan_TheSameScanBecomesValid()
    {
        // Arrange
        var clock = new FakeTimeProvider(TestTime.Now);
        var scannedAt = TestTime.Now.AddMinutes(10);
        var attribute = new NotInFutureAttribute();
        var refusedBefore = attribute.GetValidationResult(scannedAt, Context(clock));

        // Act
        clock.Advance(TimeSpan.FromMinutes(5));
        var acceptedAfter = attribute.GetValidationResult(scannedAt, Context(clock));

        // Assert: "now" comes from the injected clock, not the system clock.
        Assert.NotNull(refusedBefore);
        Assert.Equal(ValidationResult.Success, acceptedAfter);
    }

    private static ValidationContext Context(TimeProvider? clock = null) =>
        new(new object(), new Services(clock ?? new FakeTimeProvider(TestTime.Now)), null)
        {
            MemberName = "ScannedAtUtc",
            DisplayName = "scannedAtUtc",
        };

    private sealed class Services(TimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? clock : null;
    }
}
