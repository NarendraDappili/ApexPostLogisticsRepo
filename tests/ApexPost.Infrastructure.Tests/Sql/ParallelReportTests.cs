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
        var page = await new ParallelBranchReportPageQueries(contexts).GetAsync(data.Branch, Today, lowStockThreshold: 2);

        Assert.Equal(4, contexts.Created.Count);
        Assert.Equal(4, contexts.Created.Distinct().Count());
        Assert.All(contexts.Created, context => Assert.Throws<ObjectDisposedException>(() => context.Model));

        await using var sequential = database.CreateContext();
        var reports = new EfBranchReportQueries(sequential);
        Assert.Equal(await reports.GetDailySalesAsync(data.Branch, Today), page.DailySales);
        Assert.Equal((await reports.GetLowStockAsync(data.Branch, 2)).Items, page.LowStock.Items);
        Assert.Equal((await reports.GetShippingVolumeAsync(data.Branch)).Items, page.ShippingVolume.Items);
        Assert.Equal(await reports.GetMoneyOrderVerificationAsync(data.Branch), page.MoneyOrders);
        Assert.Equal((1, 10m, 1m), (page.DailySales.PaidTransactionCount, page.DailySales.RetailSales.Amount, page.DailySales.Tax.Amount));
    }

    [Fact]
    public async Task A_report_that_fails_fails_the_page_with_its_business_error()
    {
        var page = new ParallelBranchReportPageQueries(new GatedContextFactory(database, expected: 1));

        var failure = await Assert.ThrowsAsync<Application.Abstractions.RuleViolationException>(() =>
            page.GetAsync(new Domain.ValueObjects.BranchCode("BR-NOWHERE"), Today, lowStockThreshold: 2));

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
