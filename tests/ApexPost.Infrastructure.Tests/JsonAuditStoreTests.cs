using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;
using System.Text.Json;

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

    [Fact]
    public async Task Appends_add_one_json_line_without_rewriting_earlier_lines()
    {
        var store = new JsonAuditStore(new JsonRepositoryOptions { DataDirectory = _directory });
        await store.AppendAsync(CreateRecord(1));
        var path = Path.Combine(_directory, JsonAuditStore.FileName);
        var before = await File.ReadAllBytesAsync(path);

        await store.AppendAsync(CreateRecord(2));

        var after = await File.ReadAllBytesAsync(path);
        Assert.Equal(before, after.Take(before.Length));
        var lines = (await File.ReadAllLinesAsync(path)).Where(line => line.Length > 0).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(line).RootElement.ValueKind));
    }

    [Fact]
    public async Task Cancellation_is_distinct_from_a_malformed_audit_file_failure()
    {
        var store = new JsonAuditStore(new JsonRepositoryOptions { DataDirectory = _directory });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AppendAsync(CreateRecord(1), cancellation.Token));
        Assert.Empty(await store.ReadAsync());

        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, JsonAuditStore.FileName), "not-json\n");
        await Assert.ThrowsAnyAsync<JsonException>(() => store.ReadAsync());
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
