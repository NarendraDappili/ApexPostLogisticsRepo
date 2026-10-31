using ApexPost.Application.Abstractions;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Seed;

/// <summary>
/// Fills an empty, fully migrated ApexPostDb with a fixed, fictional data set: 5 branches, 20 staff, 30 products,
/// 10 zones with 60 rates and 240 historical transactions over 60 days, so reports and indexes have real work to do.
/// It never changes a database that already has data, and it never creates or migrates the schema.
/// </summary>
public sealed class DemoDataSeeder(ApexPostDbContext db, IPasswordHasher passwordHasher, TimeProvider? timeProvider = null)
{
    public const int HistoricalTransactionCount = 240;
    private const int HistoryDays = 60;
    // Matches the default ApexPost:MoneyOrders fee configuration so seeded totals agree with live ones.
    private static readonly MoneyOrderPolicy SeedMoneyOrderPolicy = new(new Money(1000m), MoneyOrderPolicy.FlatPlusPercentageFee(new Money(2.50m), 0.01m));
    private static readonly (string Code, string Name, string SkuPrefix, string ItemName)[] Categories =
    [
        ("STAMPS", "Stamps", "STP", "Stamp Pack"),
        ("PACKAGING", "Packaging", "PKG", "Packaging Box"),
        ("STATIONERY", "Stationery", "STN", "Stationery Set"),
    ];

    /// <param name="demoPassword">Every seeded staff member gets their own salted PBKDF2 hash of this password.</param>
    public async Task<DemoSeedResult> SeedAsync(string demoPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(demoPassword);
        if ((await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
        {
            throw new InvalidOperationException("ApexPostDb has pending migrations. Apply them first with: dotnet ef database update --project src/ApexPost.Infrastructure --startup-project src/ApexPost.Infrastructure");
        }

        if (await db.Branches.AnyAsync(cancellationToken).ConfigureAwait(false)) return DemoSeedResult.AlreadySeeded;

        var data = CreateData((timeProvider ?? TimeProvider.System).GetUtcNow());
        foreach (var member in data.Staff) member.SetPasswordHash(passwordHasher.Hash(demoPassword));

        db.ProductCategories.AddRange(data.Categories);
        db.ShippingZones.AddRange(data.Zones);
        db.Branches.AddRange(data.Branches);
        db.Counters.AddRange(data.Counters);
        db.Staff.AddRange(data.Staff);
        db.Products.AddRange(data.Products);
        db.BranchStock.AddRange(data.Inventory);
        db.ShippingRates.AddRange(data.Rates);
        db.Shipments.AddRange(data.Shipments);
        db.MoneyOrders.AddRange(data.MoneyOrders.Select(MoneyOrderMapper.ToEntity));
        db.CounterTransactions.AddRange(data.Transactions);
        // One commit: a partly seeded database is never left behind.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new DemoSeedResult(false, data.Branches.Count, data.Staff.Count, data.Products.Count, data.Rates.Count, data.Transactions.Count);
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
        var categories = Categories.Select(category => new ProductCategory(category.Code, category.Name)).ToArray();
        var products = Enumerable.Range(1, 30).Select(index =>
        {
            var category = Categories[(index - 1) % Categories.Length];
            return new Product(new ProductId($"PRD-{index:000}"), $"Demo {category.ItemName} {index:00}", new Money(5m + index * 1.25m), new Sku($"{category.SkuPrefix}-{index:000}"), category.Code);
        }).ToArray();
        // The first three products start at or below the default low-stock threshold so the report has content.
        var inventory = branches.SelectMany((branch, branchIndex) => products.Select((product, productIndex) =>
            new BranchInventoryItem(branch.Code, product.Id, productIndex < 3 ? 4 + productIndex + branchIndex : 100 + branchIndex * 10 + productIndex))).ToArray();
        var zones = Enumerable.Range(1, 10).Select(index => new ShippingZoneEntity { ZoneCode = new ZoneCode($"ZONE-{index:00}"), Name = $"Demo Zone {index:00}" }).ToArray();
        var rates = zones.Select(zone => zone.ZoneCode).SelectMany(CreateRates).ToArray();
        var history = CreateHistory(branches, staff, products, zones.Select(zone => zone.ZoneCode).ToArray(), rates, now);
        return new DemoData(categories, zones, branches, counters, staff, products, inventory, rates, history.Shipments, history.MoneyOrders, history.Transactions);
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
    /// Four paid baskets a day for sixty days, rotating through the branches, so on any one day at least one branch
    /// has no sales. Half the baskets carry a shipment and a quarter a money order; a few are refunded or voided,
    /// and every branch has one turned-away money order for the auditor view.
    /// </summary>
    private static (List<Shipment> Shipments, List<MoneyOrder> MoneyOrders, List<CounterTransaction> Transactions) CreateHistory(
        IReadOnlyList<Branch> branches, IReadOnlyList<StaffMember> staff, IReadOnlyList<Product> products, IReadOnlyList<ZoneCode> zones, IReadOnlyList<ShippingRate> rates, DateTimeOffset now)
    {
        var shipments = new List<Shipment>();
        var moneyOrders = new List<MoneyOrder>();
        var transactions = new List<CounterTransaction>();
        var perDay = HistoricalTransactionCount / HistoryDays;
        for (var index = 0; index < HistoricalTransactionCount; index++)
        {
            var branch = branches[index % branches.Count];
            var clerks = staff.Where(item => item.Role == StaffRole.CounterClerk && item.AssignedBranchCode == branch.Code).ToArray();
            var clerk = clerks[index / branches.Count % clerks.Length];
            var manager = staff.First(item => item.Role == StaffRole.BranchManager && item.AssignedBranchCode == branch.Code);
            var transactionId = new CounterTransactionId($"TX-{index + 1:0000}");
            var product = products[index * 7 % products.Count];
            var quantity = index % 3 + 1;
            var lines = new List<CounterTransactionLine> { new(product.Id, product.Name, quantity, product.UnitPrice) };

            if (index % 2 == 0)
            {
                var level = (ShippingServiceLevel)(index / 2 % 3);
                var type = index % 4 == 0 ? ShipmentType.Parcel : ShipmentType.Letter;
                var shipment = new Shipment(new($"SHP-{index + 1:0000}"), branch.Code, zones[index % zones.Count], new(1.5m), new(30m, 20m, 10m), type, level, transactionId);
                var price = ShippingPriceCalculator.Calculate(new ShippingPricingRequest(shipment, type, level), rates);
                shipments.Add(shipment);
                lines.Add(new(null, $"{level} {type.ToString().ToLowerInvariant()} {shipment.Id.Value} to {shipment.DestinationZoneCode.Value}", 1, price));
            }

            if (index % 4 == 1)
            {
                var amount = new Money(index % 8 == 1 ? 1500m : 300m);
                var order = new MoneyOrder(new($"MO-{index + 1:0000}"), branch.Code, amount, $"Synthetic Sender {index + 1:000}", $"Synthetic Recipient {index + 1:000}", SeedMoneyOrderPolicy.CalculateFee(amount), new(transactionId.Value));
                if (order.RequiresVerification(SeedMoneyOrderPolicy.HighValueThreshold))
                {
                    order.RecordIdentityVerification(new(order.TransactionContextId, "Passport", MaskedIdentityDocumentReference.FromMaskedValue("****4821"), IdentityVerificationOutcome.Verified));
                }

                moneyOrders.Add(order);
                lines.Add(new(null, $"Money order {order.Id.Value}", 1, order.Total));
            }

            var subtotal = lines.Select(line => line.Total).Total;
            // Always in the past, a few hours apart within each day.
            var occurredAt = now.AddDays(-(index / perDay)).AddMinutes(-(30 + index % perDay * 105));
            // Tax is zero to match the default ApexPost:Transactions:TaxRate.
            var transaction = new CounterTransaction(transactionId, branch.Code, new($"C-{index % 2 + 1:00}"), clerk.Id, lines, subtotal, new Money(0m), (PaymentMethod)(index % 3), occurredAt);
            transaction.MarkPaid();
            if (index % 37 == 17) transaction.Refund(manager.Id, "Customer returned unused items.");
            else if (index % 53 == 23) transaction.Void(manager.Id, "Basket keyed at the wrong counter.");
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
        IReadOnlyList<ProductCategory> Categories, IReadOnlyList<ShippingZoneEntity> Zones, IReadOnlyList<Branch> Branches, IReadOnlyList<Counter> Counters,
        IReadOnlyList<StaffMember> Staff, IReadOnlyList<Product> Products, IReadOnlyList<BranchInventoryItem> Inventory, IReadOnlyList<ShippingRate> Rates,
        IReadOnlyList<Shipment> Shipments, IReadOnlyList<MoneyOrder> MoneyOrders, IReadOnlyList<CounterTransaction> Transactions);
}

public sealed record DemoSeedResult(bool WasAlreadySeeded, int BranchCount, int StaffCount, int ProductCount, int RateCount, int HistoricalTransactionCount)
{
    public static DemoSeedResult AlreadySeeded { get; } = new(true, 0, 0, 0, 0, 0);
}
