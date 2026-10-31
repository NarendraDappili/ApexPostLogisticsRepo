using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class Product : IEquatable<Product>
{
    /// <param name="sku">The shelf SKU; when omitted the product identifier doubles as the SKU.</param>
    /// <param name="categoryCode">The owning category; when omitted the product is filed under <see cref="ProductCategory.GeneralCode"/>.</param>
    public Product(ProductId id, string name, Money unitPrice, Sku? sku = null, string? categoryCode = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(unitPrice);

        Id = id;
        Name = name.Trim();
        UnitPrice = unitPrice;
        Sku = sku ?? new Sku(id.Value);
        CategoryCode = ProductCategory.NormalizeCode(categoryCode ?? ProductCategory.GeneralCode);
    }

    // Used only when a stored product is materialized; the price is loaded afterwards as a complex value.
    private Product(ProductId id, string name, Sku sku, string categoryCode)
    {
        Id = id;
        Name = name;
        Sku = sku;
        CategoryCode = categoryCode;
        UnitPrice = null!;
    }

    public ProductId Id { get; }

    public Sku Sku { get; }

    public string Name { get; }

    public string CategoryCode { get; private set; }

    public Money UnitPrice { get; private set; }

    public void MoveToCategory(string categoryCode) => CategoryCode = ProductCategory.NormalizeCode(categoryCode);

    public void ChangeUnitPrice(Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        UnitPrice = unitPrice;
    }

    public bool Equals(Product? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Product other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
