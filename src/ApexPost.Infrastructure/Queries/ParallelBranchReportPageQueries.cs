using ApexPost.Application.Reports;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Queries;

/// <summary>
/// Runs the four branch reports in parallel with Task.WhenAll (ADR 0021). A DbContext allows one operation at a time,
/// so each report gets its own short-lived context, and its own pooled connection, from the factory. The request's
/// scoped context is never shared across the parallel reads. The reports only read, so no transaction or unit of
/// work spans them; with READ_COMMITTED_SNAPSHOT each one sees committed data.
/// </summary>
public sealed class ParallelBranchReportPageQueries(IDbContextFactory<ApexPostDbContext> contexts) : IBranchReportPageQueries
{
    public async Task<BranchReportPage> GetAsync(BranchCode branchCode, DateOnly from, DateOnly to, int lowStockThreshold, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentOutOfRangeException.ThrowIfNegative(lowStockThreshold);
        BranchSalesPeriodReport.Validate(from, to);

        // One day is the brief's daily report (usp_DailyBranchSales); a longer period uses usp_BranchSalesForPeriod.
        var sales = from == to
            ? RunAsync(async (reports, token) => BranchSalesPeriodReport.ForDay(await reports.GetDailySalesAsync(branchCode, from, token).ConfigureAwait(false)), cancellationToken)
            : RunAsync((reports, token) => reports.GetSalesForPeriodAsync(branchCode, from, to, token), cancellationToken);
        var lowStock = RunAsync((reports, token) => reports.GetLowStockAsync(branchCode, lowStockThreshold, token), cancellationToken);
        var shippingVolume = RunAsync((reports, token) => reports.GetShippingVolumeAsync(branchCode, token), cancellationToken);
        var moneyOrders = RunAsync((reports, token) => reports.GetMoneyOrderVerificationAsync(branchCode, token), cancellationToken);

        await Task.WhenAll(sales, lowStock, shippingVolume, moneyOrders).ConfigureAwait(false);
        return new BranchReportPage(await sales.ConfigureAwait(false), await lowStock.ConfigureAwait(false), await shippingVolume.ConfigureAwait(false), await moneyOrders.ConfigureAwait(false));
    }

    private async Task<T> RunAsync<T>(Func<EfBranchReportQueries, CancellationToken, Task<T>> report, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await report(new EfBranchReportQueries(db), cancellationToken).ConfigureAwait(false);
    }
}
