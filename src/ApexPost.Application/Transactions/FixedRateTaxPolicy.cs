using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Transactions;

/// <summary>Explicit caller-supplied tax policy; it does not define a production rate.</summary>
public sealed class FixedRateTaxPolicy : ITaxPolicy
{
    private readonly decimal _rate;
    public FixedRateTaxPolicy(decimal rate)
    {
        if (rate < 0) throw new ArgumentOutOfRangeException(nameof(rate));
        _rate = rate;
    }
    public Money Calculate(Money subtotal)
    {
        ArgumentNullException.ThrowIfNull(subtotal);
        return new Money(subtotal.Amount * _rate).RoundedToCents;
    }
}
