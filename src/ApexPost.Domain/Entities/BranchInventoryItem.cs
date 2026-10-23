using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

/// <summary>
/// Tracks the quantity of one product held by one branch.
/// </summary>
public sealed class BranchInventoryItem : IEquatable<BranchInventoryItem>
{
    public BranchInventoryItem(BranchCode branchCode, ProductId productId, int quantity = 0)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(productId);
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);

        BranchCode = branchCode;
        ProductId = productId;
        Quantity = quantity;
    }

    public BranchCode BranchCode { get; }

    public ProductId ProductId { get; }

    public int Quantity { get; private set; }

    public void AddStock(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        Quantity = checked(Quantity + quantity);
    }

    public void RemoveStock(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        if (quantity > Quantity)
        {
            throw new InvalidOperationException("Stock cannot be reduced below zero.");
        }

        Quantity -= quantity;
    }

    public bool Equals(BranchInventoryItem? other) =>
        other is not null && BranchCode == other.BranchCode && ProductId == other.ProductId;

    public override bool Equals(object? obj) => obj is BranchInventoryItem other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BranchCode, ProductId);
}
