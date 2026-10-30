using System.Collections.Concurrent;
using ApexPost.Application.Abstractions;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Repositories;
using ApexPost.Infrastructure.Security;

namespace ApexPost.Infrastructure.Seed;

/// <summary>Creates a fixed, synthetic data set in a new JSON data directory.</summary>
public sealed class DemoDataSeeder
{
    public const string SeedVersion = "apexpost-demo-v2";
    private const string ManifestFileName = "apexpost-demo-seed.json";
    private const string DataProtectionKeysFolder = "data-protection-keys";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);
    // Matches the default ApexPost:MoneyOrders fee configuration so seeded totals agree with live ones.
    private static readonly MoneyOrderPolicy SeedMoneyOrderPolicy = new(new Money(1000m), MoneyOrderPolicy.FlatPlusPercentageFee(new Money(2.50m), 0.01m));
    private readonly JsonRepositoryOptions _options;
    private readonly string? _demoPassword;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;

    /// <param name="demoPassword">When supplied, every seeded staff member gets a PBKDF2 hash of this password so the demo can sign in.</param>
    public DemoDataSeeder(JsonRepositoryOptions options, string? demoPassword = null, IPasswordHasher? passwordHasher = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        _options = options;
        _demoPassword = string.IsNullOrEmpty(demoPassword) ? null : demoPassword;
        _passwordHasher = passwordHasher ?? new Pbkdf2PasswordHasher();
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            // The web host creates its Data Protection key ring here before seeding runs; keys are not business data.
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(entry => !string.Equals(Path.GetFileName(entry), DataProtectionKeysFolder, StringComparison.OrdinalIgnoreCase)))
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

            var data = CreateData(_timeProvider.GetUtcNow());
            if (_demoPassword is not null)
            {
                foreach (var member in data.Staff) member.SetPasswordHash(_passwordHasher.Hash(_demoPassword));
            }

            await SaveEntitiesAsync(data, cancellationToken).ConfigureAwait(false);
            await new JsonFileStore<List<ShippingRateRecord>>(_options, "shipping-rates.json")
                .UpdateAsync(static () => [], items => items.AddRange(data.Rates.Select(ShippingRateRecord.From)), cancellationToken).ConfigureAwait(false);
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
        var shipments = new JsonShipmentRepository(_options);
        var moneyOrders = new JsonMoneyOrderRepository(_options);
        var transactions = new JsonCounterTransactionRepository(_options);

        foreach (var item in data.Branches) await branches.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Counters) await counters.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Staff) await staff.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Products) await products.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Inventory) await inventory.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Shipments) await shipments.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.MoneyOrders) await moneyOrders.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        foreach (var item in data.Transactions) await transactions.SaveAsync(item, cancellationToken).ConfigureAwait(false);
    }

    private static DemoData CreateData(DateTimeOffset now)
    {
        var branches = new[]
        {
            new Branch(new("BR-NORTH"), "Demo North Branch", "North", "Mon-Fri 08:30-17:30; Sat 09:00-12:30"),
            new Branch(new("BR-SOUTH"), "Demo South Branch", "South", "Mon-Fri 09:00-17:30"),
            new Branch(new("BR-EAST"), "Demo East Branch", "East", "Mon-Fri 08:00-18:00; Sat 09:00-13:00"),
            new Branch(new("BR-WEST"), "Demo West Branch", "West", "Mon-Fri 09:00-17:00"),
            new Branch(new("BR-CENTRAL"), "Demo Central Branch", "Central", "Mon-Sat 08:00-20:00")
        };
        var counters = branches.SelectMany(branch => new[] { new Counter(new("C-01"), branch.Code), new Counter(new("C-02"), branch.Code) }).ToArray();
        var staff = CreateStaff(branches);
        var products = Enumerable.Range(1, 30)
            .Select(index => new Product(new ProductId($"PRD-{index:000}"), $"Demo Retail Item {index:00}", new Money(5m + index * 1.25m)))
            .ToArray();
        // The first three products start at or below the default low-stock threshold so the report has content.
        var inventory = branches.SelectMany((branch, branchIndex) => products.Select((product, productIndex) =>
            new BranchInventoryItem(branch.Code, product.Id, productIndex < 3 ? 4 + productIndex + branchIndex : 100 + branchIndex * 10 + productIndex))).ToArray();
        var zones = Enumerable.Range(1, 10).Select(index => new ZoneCode($"ZONE-{index:00}")).ToArray();
        var rates = zones.SelectMany(CreateRates).ToArray();
        var history = CreateHistory(branches, staff, products, zones, rates, now);
        return new DemoData(branches, counters, staff, products, inventory, rates, history.Shipments, history.MoneyOrders, history.Transactions);
    }

    private static StaffMember[] CreateStaff(IReadOnlyList<Branch> branches)
    {
        var staff = new List<StaffMember>();
        for (var branchIndex = 0; branchIndex < branches.Count; branchIndex++)
        {
            staff.Add(Member($"STF-CLERK-{branchIndex + 1:00}-A", $"Demo Clerk {branchIndex + 1:00}A", StaffRole.CounterClerk, branches[branchIndex]));
            staff.Add(Member($"STF-CLERK-{branchIndex + 1:00}-B", $"Demo Clerk {branchIndex + 1:00}B", StaffRole.CounterClerk, branches[branchIndex]));
            staff.Add(Member($"STF-MANAGER-{branchIndex + 1:00}", $"Demo Manager {branchIndex + 1:00}", StaffRole.BranchManager, branches[branchIndex]));
        }

        staff.AddRange([
            Member("STF-AUDITOR-01", "Demo Auditor 01", StaffRole.Auditor, branches[0]),
            Member("STF-AUDITOR-02", "Demo Auditor 02", StaffRole.Auditor, branches[1]),
            Member("STF-AUDITOR-03", "Demo Auditor 03", StaffRole.Auditor, branches[2]),
            Member("STF-ADMIN-01", "Demo Administrator 01", StaffRole.Administrator, branches[3]),
            Member("STF-ADMIN-02", "Demo Administrator 02", StaffRole.Administrator, branches[4])
        ]);
        return [.. staff];

        static StaffMember Member(string id, string name, StaffRole role, Branch branch) =>
            new(new(id), name, role, branch.Code, $"{id.ToLowerInvariant()}@staff.apexpost.example");
    }

    private static IEnumerable<ShippingRate> CreateRates(ZoneCode zone, int zoneIndex)
    {
        foreach (var type in Enum.GetValues<ShipmentType>())
        foreach (var service in Enum.GetValues<ShippingServiceLevel>())
        {
            yield return new ShippingRate(type, service, zone, new Money(8m + zoneIndex + (int)service * 3m + (int)type * 2m), new ParcelWeight(5m + (int)service * 5m));
        }
    }

    /// <summary>
    /// Forty paid baskets over the last four days (two per branch per day), mixing retail items with shipments and
    /// money orders, plus one refund, one void and one turned-away money order per branch for the auditor view.
    /// </summary>
    private static (List<Shipment> Shipments, List<MoneyOrder> MoneyOrders, List<CounterTransaction> Transactions) CreateHistory(
        IReadOnlyList<Branch> branches, IReadOnlyList<StaffMember> staff, IReadOnlyList<Product> products, IReadOnlyList<ZoneCode> zones, IReadOnlyList<ShippingRate> rates, DateTimeOffset now)
    {
        var shipments = new List<Shipment>();
        var moneyOrders = new List<MoneyOrder>();
        var transactions = new List<CounterTransaction>();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        for (var index = 0; index < 40; index++)
        {
            var branch = branches[index % branches.Count];
            var clerks = staff.Where(item => item.Role == StaffRole.CounterClerk && item.AssignedBranchCode == branch.Code).ToArray();
            var clerk = clerks[index / branches.Count % clerks.Length];
            var manager = staff.First(item => item.Role == StaffRole.BranchManager && item.AssignedBranchCode == branch.Code);
            var transactionId = new CounterTransactionId($"TX-{index + 1:000}");
            var product = products[index % products.Count];
            var quantity = index % 3 + 1;
            var lines = new List<CounterTransactionLine> { new(product.Id, product.Name, quantity, product.UnitPrice) };

            if (index % 2 == 0)
            {
                var level = (ShippingServiceLevel)(index / 2 % 3);
                var type = index % 4 == 0 ? ShipmentType.Parcel : ShipmentType.Letter;
                var shipment = new Shipment(new($"SHP-{index + 1:000}"), branch.Code, zones[index % zones.Count], new(1.5m), new(30m, 20m, 10m), type, level, transactionId);
                var price = ShippingPriceCalculator.Calculate(new ShippingPricingRequest(shipment, type, level), rates);
                shipments.Add(shipment);
                lines.Add(new(null, $"{level} {type.ToString().ToLowerInvariant()} {shipment.Id.Value} to {shipment.DestinationZoneCode.Value}", 1, price));
            }

            if (index % 4 == 1)
            {
                var amount = new Money(index % 8 == 1 ? 1500m : 300m);
                var order = new MoneyOrder(new($"MO-{index + 1:000}"), branch.Code, amount, $"Synthetic Sender {index + 1:00}", $"Synthetic Recipient {index + 1:00}", SeedMoneyOrderPolicy.CalculateFee(amount), new(transactionId.Value));
                if (amount.Amount > SeedMoneyOrderPolicy.HighValueThreshold.Amount)
                {
                    order.RecordIdentityVerification(new(order.TransactionContextId, "Passport", MaskedIdentityDocumentReference.FromMaskedValue("****4821"), IdentityVerificationOutcome.Verified));
                }
                moneyOrders.Add(order);
                lines.Add(new(null, $"Money order {order.Id.Value}", 1, order.Total));
            }

            var subtotal = lines.Select(line => line.Total).Total;
            var occurredAt = today.AddDays(-(index / 10)).AddHours(9).AddMinutes(index % 10 * 30);
            // Tax is zero to match the default ApexPost:Transactions:TaxRate.
            var transaction = new CounterTransaction(transactionId, branch.Code, new($"C-{index % 2 + 1:00}"), clerk.Id, lines, subtotal, new Money(0m), (PaymentMethod)(index % 3), occurredAt);
            transaction.MarkPaid();
            if (index == 17) transaction.Refund(manager.Id, "Customer returned unused items.");
            if (index == 23) transaction.Void(manager.Id, "Basket keyed at the wrong counter.");
            transactions.Add(transaction);
        }

        foreach (var branch in branches)
        {
            var refused = new MoneyOrder(new($"MO-REFUSED-{branch.Code.Value}"), branch.Code, new(2000m), "Synthetic Sender", "Synthetic Recipient", SeedMoneyOrderPolicy.CalculateFee(new Money(2000m)), new($"TX-REFUSED-{branch.Code.Value}"));
            refused.RecordIdentityVerification(new(refused.TransactionContextId, "Driving licence", MaskedIdentityDocumentReference.FromMaskedValue("****0000"), IdentityVerificationOutcome.Failed));
            moneyOrders.Add(refused);
        }

        return (shipments, moneyOrders, transactions);
    }

    private sealed record DemoData(
        IReadOnlyList<Branch> Branches, IReadOnlyList<Counter> Counters, IReadOnlyList<StaffMember> Staff, IReadOnlyList<Product> Products,
        IReadOnlyList<BranchInventoryItem> Inventory, IReadOnlyList<ShippingRate> Rates, IReadOnlyList<Shipment> Shipments,
        IReadOnlyList<MoneyOrder> MoneyOrders, IReadOnlyList<CounterTransaction> Transactions);
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
