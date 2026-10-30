using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

public sealed class MoneyOrderPolicy : IMoneyOrderPolicy
{
    private readonly Func<Money, Money> _feeRule;

    /// <param name="feeRule">A configurable fee rule delegate; when omitted no fee is charged.</param>
    public MoneyOrderPolicy(Money highValueThreshold, Func<Money, Money>? feeRule = null)
    {
        ArgumentNullException.ThrowIfNull(highValueThreshold);
        HighValueThreshold = highValueThreshold;
        _feeRule = feeRule ?? (static _ => new Money(0m));
    }

    public Money HighValueThreshold { get; }

    public Money CalculateFee(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return _feeRule(amount).RoundedToCents;
    }

    /// <summary>Builds the configured "flat fee plus a percentage of the amount" rule.</summary>
    public static Func<Money, Money> FlatPlusPercentageFee(Money flatFee, decimal percentageRate)
    {
        ArgumentNullException.ThrowIfNull(flatFee);
        ArgumentOutOfRangeException.ThrowIfNegative(percentageRate);
        return amount => new Money(checked(flatFee.Amount + amount.Amount * percentageRate));
    }
}
