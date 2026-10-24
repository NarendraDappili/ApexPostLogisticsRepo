using ApexPost.Application.Reports;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Reports;

public sealed class BranchReportServiceTests
{
    [Fact]
    public async Task Reports_group_actual_branch_data_without_identity_details()
    {
        var branch = new BranchCode("BR-A");
        var day = new DateOnly(2026, 10, 24);
        var paid = Transaction("TX-1", branch, day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), CounterTransactionStatus.Paid);
        var outsideDay = Transaction("TX-2", branch, day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), CounterTransactionStatus.Paid);
        var otherBranch = Transaction("TX-3", new("BR-B"), day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), CounterTransactionStatus.Paid);
        var reportService = new BranchReportService(
            new TransactionRepo([paid, outsideDay, otherBranch]),
            new InventoryRepo([new(branch, new("P-LOW"), 2), new(branch, new("P-HIGH"), 9), new(new("BR-B"), new("P-OTHER"), 1)]),
            new ShipmentRepo([Shipment("S-1", branch, "ZONE-1", ShippingServiceLevel.Express), Shipment("S-2", branch, "ZONE-1", ShippingServiceLevel.Express), Shipment("S-3", branch, "ZONE-2", ShippingServiceLevel.Standard)]),
            new MoneyOrderRepo([Order("MO-1", branch, IdentityVerificationOutcome.Verified), Order("MO-2", branch, IdentityVerificationOutcome.Failed), Order("MO-3", new("BR-B"), IdentityVerificationOutcome.Pending)]));

        var sales = await reportService.GetDailySalesAsync(branch, day);
        var lowStock = await reportService.GetLowStockAsync(branch, 2);
        var shipping = await reportService.GetShippingVolumeAsync(branch);
        var moneyOrders = await reportService.GetMoneyOrderVerificationAsync(branch);

        Assert.Equal(10m, sales.RetailSales.Amount);
        Assert.Equal(8m, sales.ServiceSales.Amount);
        Assert.Equal(1.8m, sales.Tax.Amount);
        Assert.Equal(1, sales.PaidTransactionCount);
        Assert.Single(lowStock.Items);
        Assert.Equal("P-LOW", lowStock.Items.Single().ProductId.Value);
        Assert.Equal(2, shipping.Items.Single(item => item.DestinationZoneCode.Value == "ZONE-1").Count);
        Assert.Equal(2, moneyOrders.TotalCount);
        Assert.Equal(1, moneyOrders.VerifiedCount);
        Assert.Equal(1, moneyOrders.FailedCount);
    }

    [Fact]
    public async Task Reports_propagate_cancellation()
    {
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync();
        var service = new BranchReportService(new TransactionRepo([]), new InventoryRepo([]), new ShipmentRepo([]), new MoneyOrderRepo([]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDailySalesAsync(new("BR-A"), new(2026, 1, 1), cancellation.Token));
    }

    private static CounterTransaction Transaction(string id, BranchCode branch, DateTime date, CounterTransactionStatus status)
    {
        var transaction = new CounterTransaction(new(id), branch, new("C-1"), new("CLERK-1"), [new(new ProductId("P-1"), "Retail", 1, new(10m)), new(null, "Service", 1, new(8m))], new(18m), new(1.8m), PaymentMethod.Cash, new DateTimeOffset(date));
        if (status == CounterTransactionStatus.Paid) transaction.MarkPaid();
        return transaction;
    }
    private static Shipment Shipment(string id, BranchCode branch, string zone, ShippingServiceLevel level) => new(new(id), branch, new(zone), new(1m), new(1m, 1m, 1m), ShipmentType.Parcel, level);
    private static MoneyOrder Order(string id, BranchCode branch, IdentityVerificationOutcome outcome) { var order = new MoneyOrder(new(id), branch, new(10m), "Synthetic Sender", "Synthetic Recipient", new(1m), new($"CTX-{id}")); order.RecordIdentityVerification(new(new($"CTX-{id}"), "Demo ID", MaskedIdentityDocumentReference.FromDocumentNumber("DOC1234"), outcome)); return order; }

    private sealed class TransactionRepo(IReadOnlyCollection<CounterTransaction> items) : ICounterTransactionRepository { public Task<CounterTransaction?> FindByIdAsync(CounterTransactionId id, CancellationToken t = default) => Task.FromResult(items.FirstOrDefault(item => item.Id == id)); public Task<IReadOnlyCollection<CounterTransaction>> FindByBranchAsync(BranchCode branch, CancellationToken t = default) { t.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyCollection<CounterTransaction>>(items.Where(item => item.BranchCode == branch).ToArray()); } public Task SaveAsync(CounterTransaction t, CancellationToken c = default) => Task.CompletedTask; }
    private sealed class InventoryRepo(IReadOnlyCollection<BranchInventoryItem> items) : IInventoryRepository { public Task<BranchInventoryItem?> FindAsync(BranchCode b, ProductId p, CancellationToken t = default) => Task.FromResult(items.FirstOrDefault(item => item.BranchCode == b && item.ProductId == p)); public Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode b, CancellationToken t = default) { t.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyCollection<BranchInventoryItem>>(items.Where(item => item.BranchCode == b).ToArray()); } public Task SaveAsync(BranchInventoryItem i, CancellationToken t = default) => Task.CompletedTask; public Task<T> UpdateAsync<T>(BranchCode b, ProductId p, Func<BranchInventoryItem, T> update, CancellationToken t = default) => Task.FromResult(update(items.First(item => item.BranchCode == b && item.ProductId == p))); }
    private sealed class ShipmentRepo(IReadOnlyCollection<Shipment> items) : IShipmentRepository { public Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken t = default) => Task.FromResult(items.FirstOrDefault(item => item.Id == id)); public Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode b, CancellationToken t = default) { t.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyCollection<Shipment>>(items.Where(item => item.OriginBranchCode == b).ToArray()); } public Task SaveAsync(Shipment s, CancellationToken t = default) => Task.CompletedTask; }
    private sealed class MoneyOrderRepo(IReadOnlyCollection<MoneyOrder> items) : IMoneyOrderRepository { public Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken t = default) => Task.FromResult(items.FirstOrDefault(item => item.Id == id)); public Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode b, CancellationToken t = default) { t.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyCollection<MoneyOrder>>(items.Where(item => item.BranchCode == b).ToArray()); } public Task SaveAsync(MoneyOrder m, CancellationToken t = default) => Task.CompletedTask; }
}
