namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Represents a non-negative monetary amount. Currency and rounding policies belong to later requirements.
/// </summary>
public sealed record Money
{
    public Money(decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        Amount = amount;
    }

    public decimal Amount { get; }
}
