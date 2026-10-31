using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Queries;

// Query side of the application: screens read through these services, which return read models from
// no-tracking projections. Repositories are kept for the command side (load an aggregate, change it, commit).
// Both sides use the same database; no separate read store is needed.

public interface IBranchQueries
{
    /// <summary>The branch header plus today's activity counts; null when the branch does not exist.</summary>
    Task<BranchDashboard?> GetDashboardAsync(BranchCode branchCode, DateOnly day, int lowStockThreshold, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchListItem>> GetBranchesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetOpenCounterCodesAsync(BranchCode branchCode, CancellationToken cancellationToken = default);
}

public interface ICatalogueQueries
{
    /// <summary>Every product with its stock at <paramref name="branchCode"/>, or without stock when no branch is given.</summary>
    Task<IReadOnlyList<CatalogueItem>> GetCatalogueAsync(BranchCode? branchCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockLevel>> GetStockLevelsAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}

public interface ITransactionQueries
{
    Task<IReadOnlyList<TransactionSummary>> GetRecentAsync(BranchCode branchCode, int count, CancellationToken cancellationToken = default);
}

public interface IStaffQueries
{
    Task<IReadOnlyList<StaffListItem>> GetStaffAsync(CancellationToken cancellationToken = default);
}
