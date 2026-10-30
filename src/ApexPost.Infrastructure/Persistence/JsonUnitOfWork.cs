using ApexPost.Application.Abstractions;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// Unit of work over the JSON files. Repositories queue their writes here and nothing touches disk until
/// <see cref="SaveChangesAsync"/>. Commits are serialized in-process, and stock rows are written with an
/// optimistic quantity check, so a stale read can never be written back.
/// Unlike a database transaction, a multi-file commit that fails part-way is not rolled back; the EF Core
/// unit of work replaces this class when SQL Server becomes the system of record.
/// </summary>
public sealed class JsonUnitOfWork : IUnitOfWork
{
    private static readonly SemaphoreSlim CommitGate = new(1, 1);
    private readonly List<Func<CancellationToken, Task>> _pendingWrites = [];

    internal void Stage(Func<CancellationToken, Task> write) => _pendingWrites.Add(write);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await CommitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var writes = _pendingWrites.ToArray();
            _pendingWrites.Clear();
            // Once the first file is written the commit must finish, so the request token is not passed on.
            foreach (var write in writes)
            {
                await write(CancellationToken.None).ConfigureAwait(false);
            }

            return writes.Length;
        }
        finally
        {
            CommitGate.Release();
        }
    }

    /// <remarks>JSON files have no cross-file transaction; the operation runs as-is and commits through <see cref="SaveChangesAsync"/>.</remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation(cancellationToken);
    }
}
