using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Inventory;

public sealed record InventoryStockLevel(BranchCode BranchCode, ProductId ProductId, int Quantity);
