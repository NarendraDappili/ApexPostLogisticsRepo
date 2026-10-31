using ApexPost.Application.Abstractions;
using ApexPost.Application.Inventory;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Persistence.StoredProcedures;

/// <summary>
/// Calls usp_AdjustStock on the request's context, so the call joins the transaction opened by
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>. The interpolated values become SQL parameters.
/// </summary>
public sealed class EfStockAdjustments(ApexPostDbContext db) : IStockAdjustments
{
    public async Task<int> ApplyAsync(StockAdjustment adjustment, Sku sku, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        ArgumentNullException.ThrowIfNull(sku);
        var rows = await StoredProcedureErrors.TranslateAsync(() => db.Database
            .SqlQuery<AdjustedStock>($"EXEC dbo.usp_AdjustStock @BranchCode = {adjustment.BranchCode.Value}, @Sku = {sku.Value}, @Delta = {adjustment.Delta}, @Reason = {adjustment.Reason}, @ChangedBy = {adjustment.ChangedBy.Value}")
            .ToListAsync(cancellationToken)).ConfigureAwait(false);
        return rows.Single().NewQuantity;
    }

    private sealed class AdjustedStock
    {
        public int NewQuantity { get; init; }
    }
}

internal static class StoredProcedureErrors
{
    /// <summary>
    /// The procedures report broken business rules with THROW 50001-59999. Those become a
    /// <see cref="RuleViolationException"/> carrying the procedure's message; any other database error is left as it is.
    /// </summary>
    public static async Task<T> TranslateAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number is >= 50000 and < 60000)
        {
            throw new RuleViolationException(exception.Message, exception);
        }
    }
}
