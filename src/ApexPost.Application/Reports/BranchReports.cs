using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Reports;

/// <summary>
/// The four branch reports. Implementations aggregate in the database (projections or stored procedures)
/// instead of loading aggregates and summing them in memory.
/// </summary>
public interface IBranchReportQueries
{
    /// <summary>Paid sales on one UTC day, split into retail (catalogue products) and services (shipments, money orders).</summary>
    Task<DailyBranchSalesReport> GetDailySalesAsync(BranchCode branchCode, DateOnly day, CancellationToken cancellationToken = default);

    Task<LowStockReport> GetLowStockAsync(BranchCode branchCode, int threshold, CancellationToken cancellationToken = default);

    Task<ShippingVolumeReport> GetShippingVolumeAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    Task<MoneyOrderVerificationReport> GetMoneyOrderVerificationAsync(BranchCode branchCode, CancellationToken cancellationToken = default);
}

public sealed record DailyBranchSalesReport(BranchCode BranchCode, DateOnly Day, Money RetailSales, Money ServiceSales, Money Tax, int PaidTransactionCount)
{
    public Money TotalSales => new(RetailSales.Amount + ServiceSales.Amount + Tax.Amount);
}

public sealed record LowStockReport(BranchCode BranchCode, int Threshold, IReadOnlyCollection<LowStockItem> Items);

public sealed record LowStockItem(ProductId ProductId, string Name, int Quantity);

public sealed record ShippingVolumeReport(BranchCode BranchCode, IReadOnlyCollection<ShippingVolumeItem> Items);

public sealed record ShippingVolumeItem(ZoneCode DestinationZoneCode, ShippingServiceLevel ServiceLevel, int Count);

public sealed record MoneyOrderVerificationReport(BranchCode BranchCode, int TotalCount, int VerifiedCount, int FailedCount, int PendingCount);

/// <summary>The four reports the reports page shows together.</summary>
public sealed record BranchReportPage(
    DailyBranchSalesReport DailySales, LowStockReport LowStock, ShippingVolumeReport ShippingVolume, MoneyOrderVerificationReport MoneyOrders);

/// <summary>
/// Reads the four branch reports at once. They are independent, read-only queries, so an implementation may run them
/// in parallel, provided each one has its own database context (ADR 0021).
/// </summary>
public interface IBranchReportPageQueries
{
    Task<BranchReportPage> GetAsync(BranchCode branchCode, DateOnly day, int lowStockThreshold, CancellationToken cancellationToken = default);
}
