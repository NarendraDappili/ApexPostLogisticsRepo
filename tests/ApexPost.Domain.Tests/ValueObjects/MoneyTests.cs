using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Constructor_preserves_decimal_precision()
    {
        var money = new Money(12.3456m);

        Assert.Equal(12.3456m, money.Amount);
    }

    [Fact]
    public void Constructor_accepts_zero_boundary()
    {
        Assert.Equal(0m, new Money(0m).Amount);
    }

    [Fact]
    public void Constructor_rejects_negative_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-0.01m));
    }

    [Fact]
    public void Equivalent_amounts_compare_equal()
    {
        Assert.Equal(new Money(9.99m), new Money(9.99m));
        Assert.NotEqual(new Money(9.99m), new Money(10m));
    }

    [Theory]
    [InlineData(2.345, 2.35)]
    [InlineData(2.344, 2.34)]
    [InlineData(0.005, 0.01)]
    public void Rounded_to_cents_uses_away_from_zero_midpoint_rounding(decimal amount, decimal expected)
    {
        Assert.Equal(expected, new Money(amount).RoundedToCents.Amount);
    }

    [Fact]
    public void Total_sums_a_sequence_of_money()
    {
        Assert.Equal(6.5m, new[] { new Money(1m), new Money(2.25m), new Money(3.25m) }.Total.Amount);
        Assert.Equal(0m, Array.Empty<Money>().Total.Amount);
    }
}
