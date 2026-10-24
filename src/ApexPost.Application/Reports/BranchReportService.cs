using ApexPost.Application.Repositories;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Reports;

/// <summary>Read-only branch-scoped report queries over existing repositories.</summary>
public sealed class BranchReportService
{
    private readonly ICounterTransactionRepository _transactions;
    private readonly IInventoryRepository _inventory;
    private readonly IShipmentRepository _shipments;
    private readonly IMoneyOrderRepository _moneyOrders;

    public BranchReportService(ICounterTransactionRepository transactions, IInventoryRepository inventory, IShipmentRepository shipments, IMoneyOrderRepository moneyOrders)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _shipments = shipments ?? throw new ArgumentNullException(nameof(shipments));
        _moneyOrders = moneyOrders ?? throw new ArgumentNullException(nameof(moneyOrders));
    }

    public async Task<DailyBranchSalesReport> GetDailySalesAsync(BranchCode branchCode, DateOnly day, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        var transactions = await _transactions.FindByBranchAsync(branchCode, cancellationToken).ConfigureAwait(false);
        var matching = transactions.Where(transaction => transaction.Status == CounterTransactionStatus.Paid && DateOnly.FromDateTime(transaction.OccurredAtUtc.UtcDateTime) == day).ToArray();
        var retail = matching.SelectMany(transaction => transaction.Lines).Where(line => line.IsRetail).Sum(line => line.Total.Amount);
        var services = matching.SelectMany(transaction => transaction.Lines).Where(line => !line.IsRetail).Sum(line => line.Total.Amount);
        var tax = matching.Sum(transaction => transaction.Tax.Amount);
        return new DailyBranchSalesReport(branchCode, day, new(retail), new(services), new(tax), matching.Length);
    }

    public async Task<LowStockReport> GetLowStockAsync(BranchCode branchCode, int threshold, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        var items = await _inventory.FindByBranchAsync(branchCode, cancellationToken).ConfigureAwait(false);
        return new LowStockReport(branchCode, threshold, items.Where(item => item.Quantity <= threshold).OrderBy(item => item.ProductId.Value).Select(item => new LowStockItem(item.ProductId, item.Quantity)).ToArray());
    }

    public async Task<ShippingVolumeReport> GetShippingVolumeAsync(BranchCode branchCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        var shipments = await _shipments.FindByOriginBranchAsync(branchCode, cancellationToken).ConfigureAwait(false);
        return new ShippingVolumeReport(branchCode, shipments.GroupBy(shipment => new { shipment.DestinationZoneCode, shipment.ServiceLevel }).OrderBy(group => group.Key.DestinationZoneCode.Value).ThenBy(group => group.Key.ServiceLevel).Select(group => new ShippingVolumeItem(group.Key.DestinationZoneCode, group.Key.ServiceLevel, group.Count())).ToArray());
    }

    public async Task<MoneyOrderVerificationReport> GetMoneyOrderVerificationAsync(BranchCode branchCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        var orders = await _moneyOrders.FindByBranchAsync(branchCode, cancellationToken).ConfigureAwait(false);
        return new MoneyOrderVerificationReport(branchCode, orders.Count, orders.Count(order => order.IdentityVerificationOutcome == IdentityVerificationOutcome.Verified), orders.Count(order => order.IdentityVerificationOutcome == IdentityVerificationOutcome.Failed), orders.Count(order => order.IdentityVerificationOutcome == IdentityVerificationOutcome.Pending));
    }
}

public sealed record DailyBranchSalesReport(BranchCode BranchCode, DateOnly Day, Money RetailSales, Money ServiceSales, Money Tax, int PaidTransactionCount)
{ public Money TotalSales => new(RetailSales.Amount + ServiceSales.Amount + Tax.Amount); }
public sealed record LowStockReport(BranchCode BranchCode, int Threshold, IReadOnlyCollection<LowStockItem> Items);
public sealed record LowStockItem(ProductId ProductId, int Quantity);
public sealed record ShippingVolumeReport(BranchCode BranchCode, IReadOnlyCollection<ShippingVolumeItem> Items);
public sealed record ShippingVolumeItem(ZoneCode DestinationZoneCode, ShippingServiceLevel ServiceLevel, int Count);
public sealed record MoneyOrderVerificationReport(BranchCode BranchCode, int TotalCount, int VerifiedCount, int FailedCount, int PendingCount);
