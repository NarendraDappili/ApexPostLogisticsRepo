namespace ApexPost.Application.Inventory;

/// <summary>Configuration for inventory notifications.</summary>
public sealed class InventoryServiceOptions
{
    public required int LowStockThreshold { get; init; }

    public required string LowStockRecipient { get; init; }
}
