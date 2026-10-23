using System.Text.Json;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Application.Pricing;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Seed;

namespace ApexPost.Infrastructure.Tests;

public sealed class DemoDataSeederTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApexPost.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
    private JsonRepositoryOptions Options => new() { DataDirectory = _directory };

    [Fact]
    public async Task Seed_creates_the_required_deterministic_synthetic_data()
    {
        var result = await new DemoDataSeeder(Options).SeedAsync();

        Assert.False(result.WasAlreadySeeded);
        Assert.Equal(5, result.BranchCount);
        Assert.Equal(20, result.StaffCount);
        Assert.Equal(30, result.ProductCount);
        Assert.Equal(60, result.RateCount);
        Assert.Equal(40, result.HistoricalTransactionCount);
        Assert.True(File.Exists(Path.Combine(_directory, "apexpost-demo-seed.json")));
    }

    [Fact]
    public async Task Seed_is_idempotent_and_does_not_rewrite_completed_demo_data()
    {
        var seeder = new DemoDataSeeder(Options);
        await seeder.SeedAsync();
        var productsPath = Path.Combine(_directory, "products.json");
        var before = await File.ReadAllTextAsync(productsPath);

        var result = await seeder.SeedAsync();

        Assert.True(result.WasAlreadySeeded);
        Assert.Equal(before, await File.ReadAllTextAsync(productsPath));
    }

    [Fact]
    public async Task Seed_rejects_non_demo_data_without_modifying_it()
    {
        Directory.CreateDirectory(_directory);
        var existingPath = Path.Combine(_directory, "products.json");
        await File.WriteAllTextAsync(existingPath, "[]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoDataSeeder(Options).SeedAsync());
        Assert.Equal("[]", await File.ReadAllTextAsync(existingPath));
        Assert.False(File.Exists(Path.Combine(_directory, "apexpost-demo-seed.json")));
    }

    [Fact]
    public async Task Seeded_files_have_unique_valid_relationships_and_consistent_totals()
    {
        await new DemoDataSeeder(Options).SeedAsync();
        using var branches = await OpenAsync("branches.json");
        using var staff = await OpenAsync("staff-members.json");
        using var products = await OpenAsync("products.json");
        using var inventory = await OpenAsync("inventory.json");
        using var rates = await OpenAsync("shipping-rates.json");
        using var transactions = await OpenAsync("historical-transactions.json");

        var branchCodes = branches.RootElement.EnumerateArray().Select(item => item.GetProperty("code").GetString()!).ToHashSet(StringComparer.Ordinal);
        var staffItems = staff.RootElement.EnumerateArray().ToArray();
        var productItems = products.RootElement.EnumerateArray().ToArray();
        var inventoryItems = inventory.RootElement.EnumerateArray().ToArray();
        var rateItems = rates.RootElement.EnumerateArray().ToArray();
        var transactionItems = transactions.RootElement.EnumerateArray().ToArray();

        Assert.Equal(5, branchCodes.Count);
        Assert.Equal(20, staffItems.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(30, productItems.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(10, rateItems.Select(item => item.GetProperty("destinationZoneCode").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(40, transactionItems.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(staffItems, item => item.GetProperty("role").GetInt32() == (int)StaffRole.CounterClerk);
        Assert.Contains(staffItems, item => item.GetProperty("role").GetInt32() == (int)StaffRole.BranchManager);
        Assert.Contains(staffItems, item => item.GetProperty("role").GetInt32() == (int)StaffRole.Auditor);
        Assert.Contains(staffItems, item => item.GetProperty("role").GetInt32() == (int)StaffRole.Administrator);

        var staffBranches = staffItems.ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("branchCode").GetString()!, StringComparer.Ordinal);
        var productIds = productItems.Select(item => item.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var inventoryKeys = inventoryItems.Select(item => $"{item.GetProperty("branchCode").GetString()}:{item.GetProperty("productId").GetString()}").ToHashSet(StringComparer.Ordinal);
        Assert.All(staffBranches.Values, branch => Assert.Contains(branch, branchCodes));
        Assert.All(inventoryItems, item => Assert.True(item.GetProperty("quantity").GetInt32() >= 0));
        Assert.Equal(inventoryItems.Length, inventoryKeys.Count);

        foreach (var item in rateItems)
        {
            _ = new ShippingRate(
                (ShipmentType)item.GetProperty("shipmentType").GetInt32(),
                (ShippingServiceLevel)item.GetProperty("serviceLevel").GetInt32(),
                new ZoneCode(item.GetProperty("destinationZoneCode").GetString()!),
                new Money(item.GetProperty("charge").GetDecimal()),
                new ParcelWeight(item.GetProperty("maximumWeightKilograms").GetDecimal()));
        }

        foreach (var item in transactionItems)
        {
            var branch = item.GetProperty("branchCode").GetString()!;
            var staffId = item.GetProperty("staffId").GetString()!;
            var productId = item.GetProperty("productId").GetString()!;
            var quantity = item.GetProperty("quantity").GetInt32();
            Assert.Contains(branch, branchCodes);
            Assert.Equal(branch, staffBranches[staffId]);
            Assert.Contains(productId, productIds);
            Assert.True(quantity > 0);
            Assert.Equal(item.GetProperty("unitPrice").GetDecimal() * quantity, item.GetProperty("total").GetDecimal());
            Assert.Contains($"{branch}:{productId}", inventoryKeys);
        }
    }

    private async Task<JsonDocument> OpenAsync(string fileName) => JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, fileName)));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
