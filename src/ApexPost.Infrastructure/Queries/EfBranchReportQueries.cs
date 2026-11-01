using ApexPost.Application.Reports;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.StoredProcedures;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Queries;

/// <summary>
/// The branch reports. Daily sales and low stock call stored procedures (see ADR 0012); shipping volume and
/// money orders are no-tracking LINQ projections. Every report is one statement that aggregates in SQL Server.
/// </summary>
public sealed class EfBranchReportQueries(ApexPostDbContext db) : IBranchReportQueries
{
    public async Task<DailyBranchSalesReport> GetDailySalesAsync(BranchCode branchCode, DateOnly day, CancellationToken cancellationToken = default)
    {
        // EXEC cannot be composed with further LINQ, so the single row is materialised as a list first.
        var rows = await StoredProcedureErrors.TranslateAsync(() => db.Database
            .SqlQuery<DailySalesRow>($"EXEC dbo.usp_DailyBranchSales @BranchCode = {branchCode.Value}, @Date = {day}")
            .ToListAsync(cancellationToken)).ConfigureAwait(false);
        var row = rows.Single();
        return new DailyBranchSalesReport(branchCode, day, new Money(row.RetailSales), new Money(row.ServiceSales), new Money(row.Tax), row.PaidTransactionCount);
    }

    public async Task<LowStockReport> GetLowStockAsync(BranchCode branchCode, int threshold, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        var rows = await StoredProcedureErrors.TranslateAsync(() => db.Database
            .SqlQuery<LowStockRow>($"EXEC dbo.usp_LowStockReport @BranchCode = {branchCode.Value}, @Threshold = {threshold}")
            .ToListAsync(cancellationToken)).ConfigureAwait(false);
        return new LowStockReport(branchCode, threshold, rows.Select(row => new LowStockItem(new ProductId(row.ProductCode), row.Name, row.Quantity)).ToArray());
    }

    public async Task<ShippingVolumeReport> GetShippingVolumeAsync(BranchCode branchCode, CancellationToken cancellationToken = default)
    {
        var groups = await ShippingVolumeQuery(branchCode).ToListAsync(cancellationToken).ConfigureAwait(false);
        // Service levels are stored by name; order the few grouped rows by the enum's business order here.
        return new ShippingVolumeReport(branchCode, groups
            .OrderBy(group => group.DestinationZoneCode.Value, StringComparer.Ordinal).ThenBy(group => group.ServiceLevel)
            .ToArray());
    }

    public async Task<MoneyOrderVerificationReport> GetMoneyOrderVerificationAsync(BranchCode branchCode, CancellationToken cancellationToken = default)
    {
        var outcomes = await MoneyOrderOutcomesQuery(branchCode).ToListAsync(cancellationToken).ConfigureAwait(false);

        int CountOf(IdentityVerificationOutcome? outcome) => outcomes.Where(row => row.Outcome == outcome).Sum(row => row.Count);
        return new MoneyOrderVerificationReport(
            branchCode, outcomes.Sum(row => row.Count),
            CountOf(IdentityVerificationOutcome.Verified), CountOf(IdentityVerificationOutcome.Failed), CountOf(null));
    }

    // The two LINQ reports are built here so tests can capture their SQL with ToQueryString (PERFORMANCE.md).
    internal IQueryable<ShippingVolumeItem> ShippingVolumeQuery(BranchCode branchCode) =>
        db.Shipments.AsNoTracking()
            .Where(shipment => shipment.OriginBranchCode == branchCode)
            .GroupBy(shipment => new { shipment.DestinationZoneCode, shipment.ServiceLevel })
            .Select(group => new ShippingVolumeItem(group.Key.DestinationZoneCode, group.Key.ServiceLevel, group.Count()));

    // LEFT JOIN to the verification and GROUP BY its outcome; an order without a verification row is pending.
    internal IQueryable<OutcomeCount> MoneyOrderOutcomesQuery(BranchCode branchCode) =>
        db.MoneyOrders.AsNoTracking()
            .Where(order => order.BranchCode == branchCode)
            .Select(order => order.IdentityVerification == null ? (IdentityVerificationOutcome?)null : order.IdentityVerification.Outcome)
            .GroupBy(outcome => outcome)
            .Select(group => new OutcomeCount(group.Key, group.Count()));

    internal sealed record OutcomeCount(IdentityVerificationOutcome? Outcome, int Count);

    // Result shapes of the procedures; EF maps the result columns to these properties by name.
    private sealed class DailySalesRow
    {
        public decimal RetailSales { get; init; }
        public decimal ServiceSales { get; init; }
        public decimal Tax { get; init; }
        public int PaidTransactionCount { get; init; }
    }

    private sealed class LowStockRow
    {
        public string ProductCode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Quantity { get; init; }
    }
}
