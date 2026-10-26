using System.Text.Json;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Repositories;

namespace ApexPost.Infrastructure.Tests;

public sealed class JsonRepositoryTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApexPost.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
    private JsonRepositoryOptions Options => new() { DataDirectory = _directory };

    [Fact]
    public async Task Repositories_round_trip_all_supported_entities()
    {
        var branch = new Branch(new("blr"), "Bengaluru"); branch.Close();
        var counter = new Counter(new("01"), new("blr")); counter.Close();
        var staff = new StaffMember(new("s-1"), "Asha", StaffRole.CounterClerk, new("blr")); staff.Deactivate();
        var product = new Product(new("p-1"), "Envelope", new(12.50m));
        var inventory = new BranchInventoryItem(new("blr"), new("p-1"), 4);
        var shipment = new Shipment(new("sh-1"), new("blr"), new("z1"), new(2.5m), new(10m, 20m, 30m));
        var moneyOrder = new MoneyOrder(new("mo-1"), new("blr"), new(100m), "Synthetic Sender", "Synthetic Recipient", new(5m), new("CTX-1"));
        moneyOrder.RecordIdentityVerification(new(new("CTX-1"), "Demo ID", MaskedIdentityDocumentReference.FromDocumentNumber("ABCD1234"), IdentityVerificationOutcome.Verified));

        await new JsonBranchRepository(Options).SaveAsync(branch);
        await new JsonCounterRepository(Options).SaveAsync(counter);
        await new JsonStaffMemberRepository(Options).SaveAsync(staff);
        await new JsonProductRepository(Options).SaveAsync(product);
        await new JsonInventoryRepository(Options).SaveAsync(inventory);
        await new JsonShipmentRepository(Options).SaveAsync(shipment);
        await new JsonMoneyOrderRepository(Options).SaveAsync(moneyOrder);

        Assert.False((await new JsonBranchRepository(Options).FindByCodeAsync(new("BLR")))!.IsOpen);
        Assert.False((await new JsonCounterRepository(Options).FindByCodeAsync(new("BLR"), new("01")))!.IsOpen);
        Assert.False((await new JsonStaffMemberRepository(Options).FindByIdAsync(new("S-1")))!.IsActive);
        Assert.Equal(12.50m, (await new JsonProductRepository(Options).FindByIdAsync(new("P-1")))!.UnitPrice.Amount);
        Assert.Equal(4, (await new JsonInventoryRepository(Options).FindAsync(new("BLR"), new("P-1")))!.Quantity);
        Assert.Equal(2.5m, (await new JsonShipmentRepository(Options).FindByIdAsync(new("SH-1")))!.Weight.Kilograms);
        var storedMoneyOrder = (await new JsonMoneyOrderRepository(Options).FindByIdAsync(new("MO-1")))!;
        Assert.Equal(IdentityVerificationOutcome.Verified, storedMoneyOrder.IdentityVerificationOutcome);
        Assert.Equal("****1234", storedMoneyOrder.IdentityVerification!.MaskedDocumentReference.Value);
    }

    [Fact]
    public async Task Save_replaces_an_existing_entity_with_the_same_identity()
    {
        var repository = new JsonProductRepository(Options);
        await repository.SaveAsync(new Product(new("p-1"), "Envelope", new(10m)));
        await repository.SaveAsync(new Product(new("p-1"), "Envelope", new(15m)));

        Assert.Equal(15m, (await repository.FindByIdAsync(new("p-1")))!.UnitPrice.Amount);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "products.json")));
        Assert.Single(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Missing_file_returns_no_entity()
    {
        Assert.Null(await new JsonBranchRepository(Options).FindByCodeAsync(new("none")));
    }

    [Fact]
    public async Task Empty_or_malformed_json_is_reported_without_replacing_the_file()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "branches.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var repository = new JsonBranchRepository(Options);

        await Assert.ThrowsAsync<JsonException>(() => repository.FindByCodeAsync(new("blr")));
        Assert.Equal("{ not json", await File.ReadAllTextAsync(path));
        await File.WriteAllTextAsync(path, string.Empty);
        await Assert.ThrowsAsync<JsonException>(() => repository.FindByCodeAsync(new("blr")));
    }

    [Fact]
    public async Task Failed_replacement_preserves_existing_target_and_removes_temporary_file()
    {
        Directory.CreateDirectory(_directory);
        var targetPath = Path.Combine(_directory, "products.json");
        Directory.CreateDirectory(targetPath);
        var repository = new JsonProductRepository(Options);

        var failure = await Record.ExceptionAsync(() => repository.SaveAsync(new Product(new("p-1"), "Envelope", new(10m))));

        Assert.NotNull(failure);
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(targetPath));
        Assert.Empty(Directory.EnumerateFiles(_directory, ".products.json.*.tmp"));
    }

    [Fact]
    public async Task Cancellation_is_propagated_before_file_access()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JsonProductRepository(Options).FindByIdAsync(new("p-1"), cancellation.Token));
    }

    [Fact]
    public async Task Concurrent_saves_to_one_file_preserve_all_entities()
    {
        var repository = new JsonProductRepository(Options);
        var saves = Enumerable.Range(0, 40)
            .Select(i => repository.SaveAsync(new Product(new($"p-{i}"), $"Product {i}", new(i + 1))));
        await Task.WhenAll(saves);

        for (var i = 0; i < 40; i++)
        {
            Assert.NotNull(await repository.FindByIdAsync(new($"p-{i}")));
        }
    }

    [Fact]
    public async Task Serialized_money_order_contains_no_full_identity_document_data()
    {
        var moneyOrder = new MoneyOrder(new("mo-1"), new("blr"), new(25m), "Synthetic Sender", "Synthetic Recipient", new(0m), new("CTX-1"));
        moneyOrder.RecordIdentityVerification(new(new("CTX-1"), "Demo ID", MaskedIdentityDocumentReference.FromDocumentNumber("ABCD1234"), IdentityVerificationOutcome.Verified));
        await new JsonMoneyOrderRepository(Options).SaveAsync(moneyOrder);
        var json = await File.ReadAllTextAsync(Path.Combine(_directory, "money-orders.json"));

        Assert.Contains("verificationOutcome", json, StringComparison.Ordinal);
        Assert.Contains("****1234", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCD1234", json, StringComparison.Ordinal);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
