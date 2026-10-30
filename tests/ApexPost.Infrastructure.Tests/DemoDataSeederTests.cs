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
    public async Task Seed_treats_a_folder_holding_only_the_data_protection_key_ring_as_empty()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "data-protection-keys"));

        var result = await new DemoDataSeeder(Options).SeedAsync();

        Assert.False(result.WasAlreadySeeded);
        Assert.True(File.Exists(Path.Combine(_directory, "branches.json")));
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
        using var transactions = await OpenAsync("counter-transactions.json");

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
            var staffId = item.GetProperty("clerkId").GetString()!;
            var lines = item.GetProperty("lines").EnumerateArray().ToArray();
            Assert.Contains(branch, branchCodes);
            Assert.Equal(branch, staffBranches[staffId]);
            Assert.Contains(lines, line => line.GetProperty("productId").ValueKind == JsonValueKind.String);
            foreach (var line in lines.Where(line => line.GetProperty("productId").ValueKind == JsonValueKind.String))
            {
                var productId = line.GetProperty("productId").GetString()!;
                Assert.Contains(productId, productIds);
                Assert.Contains($"{branch}:{productId}", inventoryKeys);
                Assert.True(line.GetProperty("quantity").GetInt32() > 0);
            }
            Assert.Equal(lines.Sum(line => line.GetProperty("unitPrice").GetDecimal() * line.GetProperty("quantity").GetInt32()), item.GetProperty("subtotal").GetDecimal());
        }
    }

    [Fact]
    public async Task Seeded_history_is_readable_by_the_repositories_and_feeds_every_report()
    {
        await new DemoDataSeeder(Options).SeedAsync();
        var branch = new BranchCode("BR-NORTH");
        var transactions = await new ApexPost.Infrastructure.Repositories.JsonCounterTransactionRepository(Options).FindByBranchAsync(branch);
        var shipments = await new ApexPost.Infrastructure.Repositories.JsonShipmentRepository(Options).FindByOriginBranchAsync(branch);
        var moneyOrders = await new ApexPost.Infrastructure.Repositories.JsonMoneyOrderRepository(Options).FindByBranchAsync(branch);

        Assert.Equal(8, transactions.Count);
        Assert.Contains(transactions, transaction => transaction.Status == CounterTransactionStatus.Paid && transaction.OccurredAtUtc.UtcDateTime.Date == DateTime.UtcNow.Date);
        Assert.Contains(transactions, transaction => transaction.Lines.Any(line => !line.IsRetail));
        Assert.NotEmpty(shipments);
        Assert.All(shipments, shipment => Assert.NotNull(shipment.TransactionId));
        Assert.Contains(moneyOrders, order => order.IdentityVerificationOutcome == IdentityVerificationOutcome.Verified);
        Assert.Contains(moneyOrders, order => order.IdentityVerificationOutcome == IdentityVerificationOutcome.Failed);
        Assert.All(moneyOrders, order => Assert.True(order.IdentityVerification is null || order.IdentityVerification.MaskedDocumentReference.Value.StartsWith("****", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Seed_with_a_demo_password_stores_only_verifiable_password_hashes()
    {
        var hasher = new ApexPost.Infrastructure.Security.Pbkdf2PasswordHasher(10_000);
        await new DemoDataSeeder(Options, "demo-password-123", hasher).SeedAsync();

        var staff = await new ApexPost.Infrastructure.Repositories.JsonStaffMemberRepository(Options).GetAllAsync();
        var staffFile = await File.ReadAllTextAsync(Path.Combine(_directory, "staff-members.json"));

        Assert.Equal(20, staff.Count);
        Assert.All(staff, member => Assert.True(hasher.Verify("demo-password-123", member.PasswordHash)));
        Assert.All(staff, member => Assert.False(string.IsNullOrWhiteSpace(member.Email)));
        Assert.DoesNotContain("demo-password-123", staffFile, StringComparison.Ordinal);
    }

    private async Task<JsonDocument> OpenAsync(string fileName) => JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, fileName)));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
