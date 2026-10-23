using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

public sealed class MoneyOrderPolicy : IMoneyOrderPolicy
{
    public MoneyOrderPolicy(Money highValueThreshold)
    {
        ArgumentNullException.ThrowIfNull(highValueThreshold);
        HighValueThreshold = highValueThreshold;
    }

    public Money HighValueThreshold { get; }
}
