using System.Collections.Concurrent;
using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Repositories;

namespace ApexPost.Infrastructure.Seed;

/// <summary>Creates a fixed, synthetic data set in a new JSON data directory.</summary>
public sealed class DemoDataSeeder
{
    public const string SeedVersion = "apexpost-demo-v1";
    private const string ManifestFileName = "apexpost-demo-seed.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonRepositoryOptions _options;

    public DemoDataSeeder(JsonRepositoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        _options = options;
    }

    /// <summary>
    /// Seeds only an empty or non-existent directory. A matching completed manifest makes a rerun a no-op.
    /// Existing data without that manifest is rejected to avoid changing user data.
    /// </summary>
    public async Task<DemoSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(_options.DataDirectory);
        var gate = DirectoryLocks.GetOrAdd(directory, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                var manifestPath = Path.Combine(directory, ManifestFileName);
                if (File.Exists(manifestPath))
                {
                    var manifest = await new JsonFileStore<DemoSeedManifest>(_options, ManifestFileName)
                        .ReadAsync(static () => throw new InvalidOperationException("The demo seed manifest is missing."), cancellationToken)
                        .ConfigureAwait(false);
                    if (manifest.Version == SeedVersion)
                    {
                        return DemoSeedResult.AlreadySeeded;
                    }
                }

                throw new InvalidOperationException("Demo seeding requires an empty data directory. Existing data was not modified.");
            }

            var data = CreateData();
            await SaveEntitiesAsync(data, cancellationToken).ConfigureAwait(false);
            await new JsonFileStore<List<ShippingRateRecord>>(_options, "shipping-rates.json")
                .UpdateAsync(static () => [], items => items.AddRange(data.Rates), cancellationToken).ConfigureAwait(false);
            await new JsonFileStore<List<HistoricalTransactionRecord>>(_options, "historical-transactions.json")
                .UpdateAsync(static () => [], items => items.AddRange(data.Transactions), cancellationToken).ConfigureAwait(false);
            await new JsonFileStore<DemoSeedManifest>(_options, ManifestFileName)
                .UpdateAsync(
                    static () => new DemoSeedManifest(SeedVersion, DateTimeOffset.UnixEpoch),
                    _ => { },
                    cancellationToken)
                .ConfigureAwait(false);

            return new DemoSeedResult(false, data.Branches.Count, data.Staff.Count, data.Products.Count, data.Rates.Count, data.Transactions.Count);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveEntitiesAsync(DemoData data, CancellationToken cancellationToken)
    {
        var branches = new JsonBranchRepository(_options);
        var counters = new JsonCounterRepository(_options);
        var staff = new JsonStaffMemberRepository(_options);
        var products = new JsonProductRepository(_options);
        var inventory = new JsonInventoryRepository(_options);

        foreach (var item in data.Branches) await branches.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Counters) await counters.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Staff) await staff.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Products) await products.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Inventory) await inventory.SaveAsync(item, cancellationToken).ConfigureAwait(false);
    }

    private static DemoData CreateData()
    {
        var branches = new[]
        {
            new Branch(new("BR-NORTH"), "Demo North Branch"), new Branch(new("BR-SOUTH"), "Demo South Branch"),
            new Branch(new("BR-EAST"), "Demo East Branch"), new Branch(new("BR-WEST"), "Demo West Branch"),
            new Branch(new("BR-CENTRAL"), "Demo Central Branch")
        };
        var counters = branches.SelectMany(branch => new[] { new Counter(new("C-01"), branch.Code), new Counter(new("C-02"), branch.Code) }).ToArray();
        var staff = CreateStaff(branches);
        var products = Enumerable.Range(1, 30)
            .Select(index => new Product(new ProductId($"PRD-{index:000}"), $"Demo Retail Item {index:00}", new Money(5m + index * 1.25m)))
            .ToArray();
        var inventory = branches.SelectMany((branch, branchIndex) => products.Select((product, productIndex) =>
            new BranchInventoryItem(branch.Code, product.Id, 100 + branchIndex * 10 + productIndex))).ToArray();
        var zones = Enumerable.Range(1, 10).Select(index => new ZoneCode($"ZONE-{index:00}")).ToArray();
        var rates = zones.SelectMany((zone, zoneIndex) => CreateRates(zone, zoneIndex)).ToArray();
        var transactions = CreateTransactions(branches, staff, products).ToArray();
        return new DemoData(branches, counters, staff, products, inventory, rates, transactions);
    }

    private static StaffMember[] CreateStaff(IReadOnlyList<Branch> branches)
    {
        var staff = new List<StaffMember>();
        for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            staff.Add(new StaffMember(new($"STF-CLERK-{branchIndex + 1:00}-A"), $"Demo Clerk {branchIndex + 1:00}A", StaffRole.CounterClerk, branches[branchIndex].Code));
            staff.Add(new StaffMember(new($"STF-CLERK-{branchIndex + 1:00}-B"), $"Demo Clerk {branchIndex + 1:00}B", StaffRole.CounterClerk, branches[branchIndex].Code));
            staff.Add(new StaffMember(new($"STF-MANAGER-{branchIndex + 1:00}"), $"Demo Manager {branchIndex + 1:00}", StaffRole.BranchManager, branches[branchIndex].Code));
        }

        staff.AddRange([
            new StaffMember(new("STF-AUDITOR-01"), "Demo Auditor 01", StaffRole.Auditor, branches[0].Code),
            new StaffMember(new("STF-AUDITOR-02"), "Demo Auditor 02", StaffRole.Auditor, branches[1].Code),
            new StaffMember(new("STF-AUDITOR-03"), "Demo Auditor 03", StaffRole.Auditor, branches[2].Code),
            new StaffMember(new("STF-ADMIN-01"), "Demo Administrator 01", StaffRole.Administrator, branches[3].Code),
            new StaffMember(new("STF-ADMIN-02"), "Demo Administrator 02", StaffRole.Administrator, branches[4].Code)
        ]);
        return [.. staff];
    }

    private static IEnumerable<ShippingRateRecord> CreateRates(ZoneCode zone, int zoneIndex)
    {
        foreach (var type in Enum.GetValues<ShipmentType>())
        foreach (var service in Enum.GetValues<ShippingServiceLevel>())
        {
            var rate = new ShippingRate(type, service, zone, new Money(8m + zoneIndex + (int)service * 3m + (int)type * 2m), new ParcelWeight(5m + (int)service * 5m));
            yield return ShippingRateRecord.From(rate);
        }
    }

    private static IEnumerable<HistoricalTransactionRecord> CreateTransactions(IReadOnlyList<Branch> branches, IReadOnlyList<StaffMember> staff, IReadOnlyList<Product> products)
    {
        var clerks = staff.Where(item => item.Role == StaffRole.CounterClerk).ToArray();
        for (var index = 0; index < 40; index++)
        {
            var branch = branches[index % branches.Count];
            var clerk = clerks.First(item => item.AssignedBranchCode == branch.Code);
            var product = products[index % products.Count];
            var quantity = index % 3 + 1;
            var total = checked(product.UnitPrice.Amount * quantity);
            yield return new HistoricalTransactionRecord(
                $"TX-{index + 1:000}", branch.Code.Value, clerk.Id.Value, product.Id.Value, quantity,
                product.UnitPrice.Amount, total, new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero).AddDays(index));
        }
    }

    private sealed record DemoData(IReadOnlyList<Branch> Branches, IReadOnlyList<Counter> Counters, IReadOnlyList<StaffMember> Staff, IReadOnlyList<Product> Products, IReadOnlyList<BranchInventoryItem> Inventory, IReadOnlyList<ShippingRateRecord> Rates, IReadOnlyList<HistoricalTransactionRecord> Transactions);
}

public sealed record DemoSeedResult(bool WasAlreadySeeded, int BranchCount, int StaffCount, int ProductCount, int RateCount, int HistoricalTransactionCount)
{
    public static DemoSeedResult AlreadySeeded { get; } = new(true, 5, 20, 30, 60, 40);
}

internal sealed record DemoSeedManifest(string Version, DateTimeOffset SeededAtUtc);
internal sealed record ShippingRateRecord(ShipmentType ShipmentType, ShippingServiceLevel ServiceLevel, string DestinationZoneCode, decimal Charge, decimal MaximumWeightKilograms)
{
    public static ShippingRateRecord From(ShippingRate rate) => new(rate.ShipmentType, rate.ServiceLevel, rate.DestinationZoneCode.Value, rate.Charge.Amount, rate.MaximumWeight.Kilograms);
}
internal sealed record HistoricalTransactionRecord(string Id, string BranchCode, string StaffId, string ProductId, int Quantity, decimal UnitPrice, decimal Total, DateTimeOffset OccurredAtUtc);
