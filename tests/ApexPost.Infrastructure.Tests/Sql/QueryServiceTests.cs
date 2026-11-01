using ApexPost.Application.Catalogue;
using ApexPost.Application.Transactions;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Configurations;
using ApexPost.Infrastructure.Queries;
using ApexPost.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class QueryServiceTests(SqlServerTestDatabase database)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Screen_queries_are_single_no_tracking_projections()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        var unstocked = new ProductId($"PRD-NS-{data.Branch.Value[3..]}");
        await using (var db = database.CreateContext())
        {
            db.Products.Add(new Product(unstocked, "Never stocked here", new Money(1m)));
            await db.SaveChangesAsync();
            await SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale($"TX-Q1-{data.Branch.Value}", 1));
            await SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale($"TX-Q2-{data.Branch.Value}", 1));
        }

        var log = new SqlCommandLog();
        await using var read = database.CreateContext(interceptors: log);

        var dashboard = await new EfBranchQueries(read).GetDashboardAsync(data.Branch, Today, lowStockThreshold: 3);
        Assert.Equal((1, 2, 1), (dashboard!.OpenCounters, dashboard.PaidTransactionsToday, dashboard.LowStockProducts));
        AssertOneCommand(log);

        var catalogue = await new EfCatalogueQueries(read).GetCatalogueAsync(data.Branch);
        Assert.Equal(3, catalogue.Single(item => item.ProductId == data.Product.Value).StockQuantity);
        Assert.Null(catalogue.Single(item => item.ProductId == unstocked.Value).StockQuantity);
        AssertOneCommand(log);

        // The list reads only header columns: the owned lines are not loaded once per transaction.
        var recent = await new EfTransactionQueries(read).GetRecentAsync(data.Branch, 50);
        Assert.Equal([10m, 10m], recent.Select(transaction => transaction.Total));
        AssertOneCommand(log);

        Assert.Contains(await new EfStaffQueries(read).GetStaffAsync(), staff => staff.StaffId == data.Clerk.Value && staff.Role == StaffRole.CounterClerk);
        AssertOneCommand(log);
        Assert.Equal(["C-01"], await new EfBranchQueries(read).GetOpenCounterCodesAsync(data.Branch));
        AssertOneCommand(log);

        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Reports_are_aggregated_by_the_database_and_count_only_paid_sales_of_the_day()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 10);
        var suffix = data.Branch.Value[3..];
        await using (var db = database.CreateContext())
        {
            db.Shipments.Add(new Shipment(new($"SHP-R-{suffix}"), data.Branch, data.Zone, new ParcelWeight(1m), new ParcelDimensions(10, 10, 10), ShipmentType.Parcel, ShippingServiceLevel.Standard, new($"TX-R2-{suffix}")));
            var orders = new EfMoneyOrderRepository(db);
            orders.Add(MoneyOrder("MO-P", suffix, data, outcome: null));
            orders.Add(MoneyOrder("MO-V", suffix, data, IdentityVerificationOutcome.Verified));
            orders.Add(MoneyOrder("MO-F", suffix, data, IdentityVerificationOutcome.Failed));
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var checkout = SqlTestData.CheckoutService(db);
            await checkout.CheckoutAsync(data.Sale($"TX-R1-{suffix}", 2));
            await checkout.CheckoutAsync(new CounterCheckoutRequest($"TX-R2-{suffix}", data.Branch.Value, data.Counter.Value, data.Clerk.Value, PaymentMethod.Cash, [], [$"SHP-R-{suffix}"], []));
            var refunded = await checkout.CheckoutAsync(data.Sale($"TX-R3-{suffix}", 1));
            await checkout.RefundAsync(refunded.Id, new ManagerTransactionActionRequest(data.Manager.Value, "Returned"));
        }

        var log = new SqlCommandLog();
        await using var read = database.CreateContext(interceptors: log);
        var reports = new EfBranchReportQueries(read);

        var sales = await reports.GetDailySalesAsync(data.Branch, Today);
        Assert.Equal((20m, 8m, 0m, 2), (sales.RetailSales.Amount, sales.ServiceSales.Amount, sales.Tax.Amount, sales.PaidTransactionCount));
        AssertOneCommand(log);
        Assert.Equal(0, (await reports.GetDailySalesAsync(data.Branch, Today.AddDays(-1))).PaidTransactionCount);
        log.Clear();

        var lowStock = await reports.GetLowStockAsync(data.Branch, threshold: 8);
        Assert.Equal((data.Product, "Test Envelope", 8), Assert.Single(lowStock.Items) is var item ? (item.ProductId, item.Name, item.Quantity) : default);
        AssertOneCommand(log);

        var shipping = await reports.GetShippingVolumeAsync(data.Branch);
        Assert.Equal((data.Zone, ShippingServiceLevel.Standard, 1), Assert.Single(shipping.Items) is var volume ? (volume.DestinationZoneCode, volume.ServiceLevel, volume.Count) : default);
        AssertOneCommand(log);

        var moneyOrders = await reports.GetMoneyOrderVerificationAsync(data.Branch);
        Assert.Equal((3, 1, 1, 1), (moneyOrders.TotalCount, moneyOrders.VerifiedCount, moneyOrders.FailedCount, moneyOrders.PendingCount));
        AssertOneCommand(log);
    }

    [Fact]
    public async Task Price_revision_is_one_set_based_update_limited_to_the_category()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var category = $"CAT-{suffix}";
        await using (var db = database.CreateContext())
        {
            db.ProductCategories.Add(new ProductCategory(category, "Revised Category"));
            db.Products.AddRange(
                new Product(new($"PRD-A-{suffix}"), "Revised A", new Money(10m), categoryCode: category),
                new Product(new($"PRD-B-{suffix}"), "Revised B", new Money(3.33m), categoryCode: category),
                new Product(new($"PRD-C-{suffix}"), "Untouched C", new Money(10m)));
            await db.SaveChangesAsync();
        }

        var log = new SqlCommandLog();
        int repriced;
        await using (var db = database.CreateContext(interceptors: log))
        {
            repriced = await new PriceRevisionService(new EfCatalogueBulkUpdates(db, TimeProvider.System)).ReviseAsync(new PriceRevisionRequest(category.ToLowerInvariant(), 5m));
        }

        Assert.Equal(2, repriced);
        var update = Assert.Single(log.Commands);
        Assert.StartsWith("UPDATE [p]", update.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("ROUND(", update, StringComparison.Ordinal);

        await using var check = database.CreateContext();
        var ids = new[] { new ProductId($"PRD-A-{suffix}"), new ProductId($"PRD-B-{suffix}"), new ProductId($"PRD-C-{suffix}") };
        var prices = (await check.Products.AsNoTracking()
                .Where(product => ids.Contains(product.Id))
                .Select(product => new { product.Name, product.UnitPrice.Amount, Modified = EF.Property<DateTime?>(product, ShadowColumns.ModifiedAtUtc) })
                .ToListAsync())
            .ToDictionary(product => product.Name, product => (product.Amount, product.Modified));
        Assert.Equal(10.50m, prices["Revised A"].Amount);
        Assert.Equal(3.50m, prices["Revised B"].Amount);
        Assert.Equal(10m, prices["Untouched C"].Amount);
        Assert.NotNull(prices["Revised A"].Modified);
        Assert.Null(prices["Untouched C"].Modified);
    }

    // Replaces the Week 2 report-service assertions on tax and cancellation, now that reports run in SQL Server.
    [Fact]
    public async Task Daily_sales_include_the_tax_charged_and_every_report_honours_cancellation()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 10);
        await using (var db = database.CreateContext())
        {
            await SqlTestData.CheckoutService(db, taxRate: 0.10m).CheckoutAsync(data.Sale($"TX-TAX-{data.Branch.Value[3..]}", quantity: 2));
        }

        await using var read = database.CreateContext();
        var reports = new EfBranchReportQueries(read);
        var sales = await reports.GetDailySalesAsync(data.Branch, Today);
        Assert.Equal((20m, 2m, 22m, 1), (sales.RetailSales.Amount, sales.Tax.Amount, sales.TotalSales.Amount, sales.PaidTransactionCount));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.GetDailySalesAsync(data.Branch, Today, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.GetLowStockAsync(data.Branch, threshold: 8, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.GetShippingVolumeAsync(data.Branch, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.GetMoneyOrderVerificationAsync(data.Branch, cancelled.Token));
    }

    private static void AssertOneCommand(SqlCommandLog log)
    {
        Assert.Single(log.Commands);
        log.Clear();
    }

    private static MoneyOrder MoneyOrder(string prefix, string suffix, SqlTestData data, IdentityVerificationOutcome? outcome)
    {
        var order = new MoneyOrder(new($"{prefix}-{suffix}"), data.Branch, new Money(1500m), "Sender", "Recipient", new Money(17.50m), new($"CTX-{prefix}-{suffix}"));
        if (outcome is { } recorded)
        {
            order.RecordIdentityVerification(new IdentityVerificationRecord(order.TransactionContextId, "Passport", MaskedIdentityDocumentReference.FromDocumentNumber("AB123456"), recorded));
        }

        return order;
    }
}
