namespace ApexPost.Application.Abstractions;

/// <summary>
/// Commits the changes that repositories have staged during one use case. Repositories never save;
/// each application use case calls <see cref="SaveChangesAsync"/> exactly once, after every rule has passed,
/// so a failure part-way through a use case persists nothing.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Persists every staged change atomically.</summary>
    /// <exception cref="ConcurrencyConflictException">A row changed since it was read.</exception>
    /// <exception cref="DuplicateKeyException">A unique business key is already in use.</exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> in one explicit transaction, for use cases that combine
    /// <see cref="SaveChangesAsync"/> with set-based updates or stored procedures.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);
}
