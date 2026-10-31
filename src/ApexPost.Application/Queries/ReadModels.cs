using ApexPost.Domain.Enums;

namespace ApexPost.Application.Queries;

// Read models: flat, display-ready rows built by projection queries. They carry no behaviour and are never
// written back; changes go through the command services and the unit of work.

public sealed record BranchDashboard(
    string BranchCode, string Name, string Region, string OpeningHours, bool IsOpen,
    int OpenCounters, int PaidTransactionsToday, int LowStockProducts);

public sealed record BranchListItem(string BranchCode, string Name, string Region, string OpeningHours, bool IsOpen);

public sealed record CatalogueItem(string ProductId, string Sku, string Name, string CategoryCode, decimal UnitPrice, int? StockQuantity);

public sealed record StockLevel(string ProductId, int Quantity);

public sealed record CategoryItem(string CategoryCode, string Name, int ProductCount);

public sealed record TransactionSummary(
    string TransactionId, DateTimeOffset OccurredAtUtc, string CounterCode, string ClerkId,
    decimal Total, CounterTransactionStatus Status, string? StatusReason);

public sealed record StaffListItem(string StaffId, string Name, string? Email, StaffRole Role, string BranchCode, bool IsActive);
