using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

/// <summary>Supplies the explicitly configured high-value verification threshold.</summary>
public interface IMoneyOrderPolicy
{
    Money HighValueThreshold { get; }
}
