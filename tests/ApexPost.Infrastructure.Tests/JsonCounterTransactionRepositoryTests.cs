using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Repositories;

namespace ApexPost.Infrastructure.Tests;

public sealed class JsonCounterTransactionRepositoryTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApexPost.Infrastructure.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Repository_round_trips_a_refunded_mixed_transaction()
    {
        var transaction = new CounterTransaction(new("TX-1"), new("BR-A"), new("C-1"), new("CLERK-1"),
            [new(new ProductId("P-1"), "Envelope", 2, new(10m)), new(null, "Shipping", 1, new(8m))], new(28m), new(2.8m), PaymentMethod.Cash);
        transaction.MarkPaid();
        transaction.Refund(new("MANAGER-1"), "Synthetic refund");
        var repository = new JsonCounterTransactionRepository(new JsonRepositoryOptions { DataDirectory = _directory });

        await repository.SaveAsync(transaction);
        var persisted = await repository.FindByIdAsync(new("TX-1"));

        Assert.NotNull(persisted);
        Assert.Equal(CounterTransactionStatus.Refunded, persisted!.Status);
        Assert.Equal("MANAGER-1", persisted.AuthorizedBy!.Value);
        Assert.Equal(30.8m, persisted.Total.Amount);
        Assert.Equal(2, persisted.Lines.Count);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
