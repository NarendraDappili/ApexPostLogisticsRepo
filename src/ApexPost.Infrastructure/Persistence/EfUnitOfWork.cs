using ApexPost.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// Commits everything the request's repositories staged in the shared, scoped <see cref="ApexPostDbContext"/>
/// with a single SaveChangesAsync, and turns database conflicts into application errors.
/// </summary>
public sealed class EfUnitOfWork(ApexPostDbContext db) : IUnitOfWork
{
    // SQL Server error numbers for a duplicate key in a unique index (2601) or a unique constraint (2627).
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("Someone else changed this data while your request was being processed. Reload and try again.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && UniqueViolationNumbers.Contains(sql.Number))
        {
            throw new DuplicateKeyException("A record with the same identifier already exists.", exception);
        }
    }

    /// <summary>
    /// Runs the operation in one explicit transaction inside the execution strategy, so a transient failure
    /// retries the whole unit rather than a fragment of it.
    /// </summary>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var result = await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        });
    }
}
