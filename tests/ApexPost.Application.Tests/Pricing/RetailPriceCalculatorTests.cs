using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Pricing;

public class RetailPriceCalculatorTests
{
    [Fact]
    public void Calculate_multiplies_the_current_product_price_by_quantity_without_rounding()
    {
        var product = new Product(new ProductId("product-1"), "Packaging", new Money(1.234m));

        var price = RetailPriceCalculator.Calculate(product, 3);

        Assert.Equal(new Money(3.702m), price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_rejects_non_positive_quantities(int quantity)
    {
        var product = new Product(new ProductId("product-1"), "Packaging", new Money(1m));

        Assert.Throws<ArgumentOutOfRangeException>(() => RetailPriceCalculator.Calculate(product, quantity));
    }
}
