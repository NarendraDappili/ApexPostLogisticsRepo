using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Tests;

public sealed class JsonAuditStoreTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApexPost.Infrastructure.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Concurrent_appends_preserve_every_audit_record()
    {
        var store = new JsonAuditStore(new JsonRepositoryOptions { DataDirectory = _directory });
        var writes = Enumerable.Range(0, 40).Select(index => store.AppendAsync(CreateRecord(index)));

        await Task.WhenAll(writes);

        var records = await store.ReadAsync();
        Assert.Equal(40, records.Count);
        Assert.Equal(40, records.Select(record => record.CorrelationId).Distinct(StringComparer.Ordinal).Count());
    }

    private static AuditRecord CreateRecord(int index) => new(
        DateTimeOffset.UtcNow, "STF-TEST", "CounterClerk", "BR-NORTH", "CounterTransactions", "New",
        new Dictionary<string, string> { ["transactionId"] = $"TX-{index}" }, 302, 12, $"test-{index}", "Succeeded");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
