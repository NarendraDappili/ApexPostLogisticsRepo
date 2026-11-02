using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class ParallelReportTests(SqlServerTestDatabase database)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task The_four_reports_run_at_the_same_time_each_on_its_own_context_and_match_the_sequential_reads()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 2);
        await using (var db = database.CreateContext())
        {
            await SqlTestData.CheckoutService(db, taxRate: 0.1m).CheckoutAsync(data.Sale($"TX-PAR-{data.Branch.Value}", 1));
        }

        // Every report must have asked for its context before any is handed out: run one after another, the first
        // report would wait here until the timeout.
        var contexts = new GatedContextFactory(database, expected: 4);
        var page = await new ParallelBranchReportPageQueries(contexts).GetAsync(data.Branch, Today, Today, lowStockThreshold: 2);

        Assert.Equal(4, contexts.Created.Count);
        Assert.Equal(4, contexts.Created.Distinct().Count());
        Assert.All(contexts.Created, context => Assert.Throws<ObjectDisposedException>(() => context.Model));

        await using var sequential = database.CreateContext();
        var reports = new EfBranchReportQueries(sequential);
        // One day is the daily report (usp_DailyBranchSales).
        Assert.Equal([await reports.GetDailySalesAsync(data.Branch, Today)], page.Sales.Days);
        Assert.Equal((await reports.GetLowStockAsync(data.Branch, 2)).Items, page.LowStock.Items);
        Assert.Equal((await reports.GetShippingVolumeAsync(data.Branch)).Items, page.ShippingVolume.Items);
        Assert.Equal(await reports.GetMoneyOrderVerificationAsync(data.Branch), page.MoneyOrders);
        Assert.Equal((1, 10m, 1m), (page.Sales.PaidTransactionCount, page.Sales.RetailSales.Amount, page.Sales.Tax.Amount));
    }

    [Fact]
    public async Task A_period_reads_its_sales_from_the_period_procedure_alongside_the_other_reports()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        await using (var db = database.CreateContext())
        {
            await SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale($"TX-PER-{data.Branch.Value}", 2));
        }

        var page = await new ParallelBranchReportPageQueries(new GatedContextFactory(database, expected: 4))
            .GetAsync(data.Branch, Today.AddDays(-6), Today, lowStockThreshold: 2);

        await using var sequential = database.CreateContext();
        var expected = await new EfBranchReportQueries(sequential).GetSalesForPeriodAsync(data.Branch, Today.AddDays(-6), Today);
        Assert.Equal(expected.Days, page.Sales.Days);
        Assert.Equal((Today.AddDays(-6), Today, 1, 20m), (page.Sales.From, page.Sales.To, page.Sales.PaidTransactionCount, page.Sales.RetailSales.Amount));
    }

    [Fact]
    public async Task A_report_that_fails_fails_the_page_with_its_business_error()
    {
        var page = new ParallelBranchReportPageQueries(new GatedContextFactory(database, expected: 1));

        var failure = await Assert.ThrowsAsync<Application.Abstractions.RuleViolationException>(() =>
            page.GetAsync(new Domain.ValueObjects.BranchCode("BR-NOWHERE"), Today, Today, lowStockThreshold: 2));

        Assert.Equal("usp_DailyBranchSales: the branch does not exist.", failure.Message);
    }

    /// <summary>Hands out a new context per call, but only once <c>expected</c> callers are waiting at the same time.</summary>
    private sealed class GatedContextFactory(SqlServerTestDatabase database, int expected) : IDbContextFactory<ApexPostDbContext>
    {
        private readonly TaskCompletionSource _allWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waiting;

        public List<ApexPostDbContext> Created { get; } = [];

        public ApexPostDbContext CreateDbContext() => throw new NotSupportedException("The reports must create their contexts asynchronously.");

        public async Task<ApexPostDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _waiting) >= expected) _allWaiting.TrySetResult();
            await _allWaiting.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var context = database.CreateContext();
            lock (Created) Created.Add(context);
            return context;
        }
    }
}
