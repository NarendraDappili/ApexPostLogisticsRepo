using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Pricing;

/// <summary>
/// Calculates a retail charge from the product's current unit price and quantity.
/// </summary>
public static class RetailPriceCalculator
{
    public static Money Calculate(Product product, int quantity)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return new Money(checked(product.UnitPrice.Amount * quantity));
    }
}
