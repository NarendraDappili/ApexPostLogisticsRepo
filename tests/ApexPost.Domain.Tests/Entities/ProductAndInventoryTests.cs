using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public class ProductAndInventoryTests
{
    [Fact]
    public void Product_uses_money_and_allows_controlled_price_change()
    {
        var product = new Product(new ProductId("prod-1"), "Packaging", new Money(4.25m));

        product.ChangeUnitPrice(new Money(5m));

        Assert.Equal(new Money(5m), product.UnitPrice);
    }

    [Fact]
    public void Products_with_same_identifier_compare_equal()
    {
        Assert.Equal(
            new Product(new ProductId("prod-1"), "Envelope", new Money(1m)),
            new Product(new ProductId("PROD-1"), "Box", new Money(2m)));
    }

    [Fact]
    public void Inventory_cannot_be_created_with_negative_stock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BranchInventoryItem(new BranchCode("BR-001"), new ProductId("PROD-1"), -1));
    }

    [Fact]
    public void Inventory_adds_and_removes_stock_without_going_negative()
    {
        var inventory = new BranchInventoryItem(new BranchCode("BR-001"), new ProductId("PROD-1"), 2);

        inventory.AddStock(3);
        inventory.RemoveStock(4);

        Assert.Equal(1, inventory.Quantity);
        Assert.Throws<InvalidOperationException>(() => inventory.RemoveStock(2));
        Assert.Equal(1, inventory.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Inventory_rejects_non_positive_stock_adjustments(int quantity)
    {
        var inventory = new BranchInventoryItem(new BranchCode("BR-001"), new ProductId("PROD-1"));

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.AddStock(quantity));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.RemoveStock(quantity));
    }
}
