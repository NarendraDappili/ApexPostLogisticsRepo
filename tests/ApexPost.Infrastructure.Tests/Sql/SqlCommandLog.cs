using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// Records every SQL command a context sends and every transaction it commits, so tests can prove how many
/// round trips an operation makes (one per screen query: no N+1) and that a write is one commit.
/// </summary>
internal sealed class SqlCommandLog : DbCommandInterceptor, IDbTransactionInterceptor
{
    public List<string> Commands { get; } = [];

    public int Commits { get; private set; }

    public void Clear()
    {
        Commands.Clear();
        Commits = 0;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Commits++;
        return Task.CompletedTask;
    }
}
