using ApexPost.Application.Abstractions;

namespace ApexPost.Application.Tests;

/// <summary>Records commits so tests can prove a use case commits once, or never when it fails.</summary>
internal sealed class SpyUnitOfWork : IUnitOfWork
{
    private int _attempts;
    private int _commits;

    public int Attempts => _attempts;

    public int Commits => _commits;

    /// <summary>When set, every commit fails with this exception.</summary>
    public Exception? FailWith { get; init; }

    /// <summary>When set, the commit cancels this source first, as if the client disconnected during the save.</summary>
    public CancellationTokenSource? CancelOnCommit { get; init; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);
        CancelOnCommit?.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWith is not null) throw FailWith;
        Interlocked.Increment(ref _commits);
        return Task.FromResult(0);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
        operation(cancellationToken);
}
