using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class Product : IEquatable<Product>
{
    public Product(ProductId id, string name, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(unitPrice);

        Id = id;
        Name = name.Trim();
        UnitPrice = unitPrice;
    }

    public ProductId Id { get; }

    public string Name { get; }

    public Money UnitPrice { get; private set; }

    public void ChangeUnitPrice(Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        UnitPrice = unitPrice;
    }

    public bool Equals(Product? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Product other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
