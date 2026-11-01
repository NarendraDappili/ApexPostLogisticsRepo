using ApexPost.Application.Queries;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Queries;

// Query services read straight from the DbContext with AsNoTracking projections into read models: each method is
// one SQL statement that returns only the columns the screen shows. This is the place where direct DbContext
// access is acceptable; the command side goes through repositories and the unit of work.

public sealed class EfBranchQueries(ApexPostDbContext db) : IBranchQueries
{
    public Task<BranchDashboard?> GetDashboardAsync(BranchCode branchCode, DateOnly day, int lowStockThreshold, CancellationToken cancellationToken = default) =>
        DashboardQuery(branchCode, day, lowStockThreshold).SingleOrDefaultAsync(cancellationToken);

    // Built here so tests can capture the SQL with ToQueryString (PERFORMANCE.md).
    internal IQueryable<BranchDashboard> DashboardQuery(BranchCode branchCode, DateOnly day, int lowStockThreshold)
    {
        var (start, end) = UtcDay.Range(day);
        // One statement: the counts are correlated subqueries, not separate round trips.
        return db.Branches.AsNoTracking()
            .Where(branch => branch.Code == branchCode)
            .Select(branch => new BranchDashboard(
                branchCode.Value, branch.Name, branch.Region, branch.OpeningHours, branch.IsOpen,
                db.Counters.Count(counter => counter.BranchCode == branchCode && counter.IsOpen),
                db.CounterTransactions.Count(transaction => transaction.BranchCode == branchCode && transaction.Status == CounterTransactionStatus.Paid
                    && transaction.OccurredAtUtc >= start && transaction.OccurredAtUtc < end),
                db.BranchStock.Count(item => item.BranchCode == branchCode && item.Quantity <= lowStockThreshold)));
    }

    public async Task<IReadOnlyList<BranchListItem>> GetBranchesAsync(CancellationToken cancellationToken = default) =>
        await db.Branches.AsNoTracking()
            .OrderBy(branch => branch.Code)
            .Select(branch => new BranchListItem(branch.Code.Value, branch.Name, branch.Region, branch.OpeningHours, branch.IsOpen))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> GetOpenCounterCodesAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        await db.Counters.AsNoTracking()
            .Where(counter => counter.BranchCode == branchCode && counter.IsOpen)
            .OrderBy(counter => counter.Code)
            .Select(counter => counter.Code.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class EfCatalogueQueries(ApexPostDbContext db) : ICatalogueQueries
{
    public async Task<IReadOnlyList<CatalogueItem>> GetCatalogueAsync(BranchCode? branchCode, CancellationToken cancellationToken = default)
    {
        if (branchCode is null)
        {
            return await db.Products.AsNoTracking()
                .OrderBy(product => product.Id)
                .Select(product => new CatalogueItem(product.Id.Value, product.Sku.Value, product.Name, product.CategoryCode, product.UnitPrice.Amount, null))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        // LEFT JOIN: a product the branch has never stocked is still listed, with no quantity.
        var branchStock = db.BranchStock.AsNoTracking().Where(item => item.BranchCode == branchCode);
        return await (
                from product in db.Products.AsNoTracking()
                join item in branchStock on product.Id equals item.ProductId into stocked
                from item in stocked.DefaultIfEmpty()
                orderby product.Id
                select new CatalogueItem(product.Id.Value, product.Sku.Value, product.Name, product.CategoryCode, product.UnitPrice.Amount, item == null ? null : item.Quantity))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StockLevel>> GetStockLevelsAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        await db.BranchStock.AsNoTracking()
            .Where(item => item.BranchCode == branchCode)
            .OrderBy(item => item.ProductId)
            .Select(item => new StockLevel(item.ProductId.Value, item.Quantity))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await db.ProductCategories.AsNoTracking()
            .OrderBy(category => category.Code)
            .Select(category => new CategoryItem(category.Code, category.Name, db.Products.Count(product => product.CategoryCode == category.Code)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class EfTransactionQueries(ApexPostDbContext db) : ITransactionQueries
{
    public async Task<IReadOnlyList<TransactionSummary>> GetRecentAsync(BranchCode branchCode, int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        // Only the header columns are read; the owned lines and payment are not loaded for a list.
        return await db.CounterTransactions.AsNoTracking()
            .Where(transaction => transaction.BranchCode == branchCode)
            .OrderByDescending(transaction => transaction.OccurredAtUtc)
            .Take(count)
            .Select(transaction => new TransactionSummary(
                transaction.Id.Value, transaction.OccurredAtUtc, transaction.CounterCode.Value, transaction.ClerkId.Value,
                transaction.Subtotal.Amount + transaction.Tax.Amount, transaction.Status, transaction.StatusReason))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class EfStaffQueries(ApexPostDbContext db) : IStaffQueries
{
    public async Task<IReadOnlyList<StaffListItem>> GetStaffAsync(CancellationToken cancellationToken = default) =>
        await db.Staff.AsNoTracking()
            .OrderBy(staff => staff.Id)
            .Select(staff => new StaffListItem(staff.Id.Value, staff.Name, staff.Email, staff.Role, staff.AssignedBranchCode.Value, staff.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>A UTC calendar day as a half-open range, so date filters stay SARGable on the datetime2 column.</summary>
internal static class UtcDay
{
    public static (DateTimeOffset Start, DateTimeOffset End) Range(DateOnly day)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (start, start.AddDays(1));
    }
}
