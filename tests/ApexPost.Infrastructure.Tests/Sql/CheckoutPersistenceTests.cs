using System.Data.Common;
using ApexPost.Application.Abstractions;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class CheckoutPersistenceTests(SqlServerTestDatabase database)
{
    [Fact]
    public async Task A_sale_is_stored_with_its_lines_payment_and_stock_decrement_in_one_commit()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        var commands = new CommandRecorder();

        await using (var db = database.CreateContext(interceptors: commands))
        {
            await SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale("TX-ONE-COMMIT", quantity: 2));
        }

        await using var check = database.CreateContext();
        var sale = await check.CounterTransactions.AsNoTracking().SingleAsync(transaction => transaction.Id == new CounterTransactionId("TX-ONE-COMMIT"));
        Assert.Equal((CounterTransactionStatus.Paid, 1, PaymentMethod.Card, 20m), (sale.Status, sale.Lines.Count, sale.Payment.Method, sale.Payment.Amount.Amount));
        Assert.Equal(3, (await check.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch)).Quantity);
        // EF may split the writes into several batches (dependent rows need generated keys), but every one of them
        // runs inside the single database transaction that one SaveChanges opens and commits.
        Assert.Single(commands, text => text == CommandRecorder.Committed);
        Assert.All(["UPDATE [BranchStock]", "INSERT INTO [CounterTransactions]", "INSERT INTO [TransactionLines]", "INSERT INTO [Payments]"],
            write => Assert.Contains(commands, text => text.Contains(write, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_sale_that_fails_part_way_through_the_commit_rolls_back_stock_and_transaction_rows_together()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        var failLast = new FailOnCommand(text => text.Contains("INSERT INTO [Payments]", StringComparison.Ordinal));

        await using (var db = database.CreateContext(maxBatchSize: 1, interceptors: failLast))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale("TX-ROLLBACK", quantity: 2)));
        }

        // The stock UPDATE and the transaction INSERT really reached SQL Server before the failure...
        Assert.Contains(failLast.Executed, text => text.StartsWith("UPDATE [BranchStock]", StringComparison.Ordinal) || text.Contains("UPDATE [BranchStock]", StringComparison.Ordinal));
        Assert.Contains(failLast.Executed, text => text.Contains("INSERT INTO [CounterTransactions]", StringComparison.Ordinal));

        // ...and were rolled back with it: no partial sale and no lost stock.
        await using var check = database.CreateContext();
        Assert.Equal(5, (await check.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch)).Quantity);
        Assert.False(await check.CounterTransactions.AnyAsync(transaction => transaction.Id == new CounterTransactionId("TX-ROLLBACK")));
        Assert.Equal(0, await check.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM TransactionLines l JOIN CounterTransactions t ON t.CounterTransactionId = l.CounterTransactionId WHERE t.TransactionCode = 'TX-ROLLBACK'").SingleAsync());
    }

    [Fact]
    public async Task Two_clerks_selling_the_last_unit_cannot_drive_stock_negative()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        await using var firstClerk = database.CreateContext();
        // The second clerk has already read the stock (1 unit) when the first clerk's sale commits,
        // just before the second clerk's own stock UPDATE reaches the database.
        var interleave = new BeforeCommand(
            text => text.Contains("UPDATE [BranchStock]", StringComparison.Ordinal),
            () => SqlTestData.CheckoutService(firstClerk).CheckoutAsync(data.Sale("TX-FIRST-CLERK", quantity: 1)));
        await using var secondClerk = database.CreateContext(interceptors: interleave);

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SqlTestData.CheckoutService(secondClerk).CheckoutAsync(data.Sale("TX-SECOND-CLERK", quantity: 1)));

        Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);
        await using var check = database.CreateContext();
        Assert.Equal(0, (await check.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch)).Quantity);
        Assert.True(await check.CounterTransactions.AnyAsync(transaction => transaction.Id == new CounterTransactionId("TX-FIRST-CLERK")));
        Assert.False(await check.CounterTransactions.AnyAsync(transaction => transaction.Id == new CounterTransactionId("TX-SECOND-CLERK")));
    }

    [Fact]
    public async Task Concurrent_sales_from_separate_requests_never_oversell_or_lose_an_update()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 3);

        var outcomes = await Task.WhenAll(Enumerable.Range(1, 8).Select(async index =>
        {
            await using var db = database.CreateContext();
            try
            {
                await SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale($"TX-RACE-{index}", quantity: 1));
                return true;
            }
            catch (InvalidOperationException)
            {
                // A concurrency conflict or "Stock cannot be reduced below zero": the sale is refused, nothing is stored.
                return false;
            }
        }));

        await using var check = database.CreateContext();
        var stock = (await check.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch)).Quantity;
        var stored = await check.CounterTransactions.CountAsync(transaction => transaction.BranchCode == data.Branch);
        Assert.InRange(stock, 0, 3);
        Assert.Equal(outcomes.Count(sold => sold), stored);
        Assert.Equal(3 - stored, stock);
    }

    /// <summary>Records the text of every command EF sends, plus transaction commits.</summary>
    private sealed class CommandRecorder : List<string>, IDbCommandInterceptor, IDbTransactionInterceptor
    {
        public const string Committed = "transaction committed";

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Add(Committed);
            return Task.CompletedTask;
        }
    }

    /// <summary>Lets every command run until the matching one, then fails as a database error would.</summary>
    private sealed class FailOnCommand(Func<string, bool> matches) : DbCommandInterceptor
    {
        public List<string> Executed { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            Check(command, result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Check(command, result);

        private ValueTask<T> Check<T>(DbCommand command, T result)
        {
            if (matches(command.CommandText)) throw new IOException("Simulated database failure part-way through the commit.");
            Executed.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Runs another request to completion just before the first matching command of this context executes.</summary>
    private sealed class BeforeCommand(Func<string, bool> matches, Func<Task> otherRequest) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!_done && matches(command.CommandText))
            {
                _done = true;
                await otherRequest();
            }

            return result;
        }
    }
}
