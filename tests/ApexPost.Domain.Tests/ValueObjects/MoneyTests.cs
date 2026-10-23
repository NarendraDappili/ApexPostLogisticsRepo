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
}
