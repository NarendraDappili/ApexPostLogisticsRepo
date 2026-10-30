namespace ApexPost.Application.Inventory;

/// <summary>Configuration for inventory low-stock alerts.</summary>
public sealed class InventoryServiceOptions
{
    public required int LowStockThreshold { get; init; }
}
