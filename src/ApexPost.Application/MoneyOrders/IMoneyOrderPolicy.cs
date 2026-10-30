using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

/// <summary>Supplies the configured high-value verification threshold and fee rule.</summary>
public interface IMoneyOrderPolicy
{
    Money HighValueThreshold { get; }

    Money CalculateFee(Money amount);
}
