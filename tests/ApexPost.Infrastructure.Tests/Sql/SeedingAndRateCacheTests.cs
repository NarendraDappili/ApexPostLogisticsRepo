using ApexPost.Application.Pricing;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Pricing;
using ApexPost.Infrastructure.Security;
using ApexPost.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>Seeding needs an empty database, so this class gets its own instead of sharing the collection's.</summary>
public sealed class SeedingAndRateCacheTests : IAsyncLifetime
{
    private readonly SqlServerTestDatabase _database = new();

    [Fact]
    public async Task Demo_seed_meets_the_week_3_volumes_once_and_stores_only_hashes_and_masked_documents()
    {
        await using (var db = _database.CreateContext())
        {
            var result = await new DemoDataSeeder(db, new Pbkdf2PasswordHasher(10_000)).SeedAsync("seed-test-password");
            Assert.Equal((false, 5, 20, 30, 60, 240), (result.WasAlreadySeeded, result.BranchCount, result.StaffCount, result.ProductCount, result.RateCount, result.HistoricalTransactionCount));
        }

        await using (var db = _database.CreateContext())
        {
            Assert.True((await new DemoDataSeeder(db, new Pbkdf2PasswordHasher(10_000)).SeedAsync("seed-test-password")).WasAlreadySeeded);
        }

        await using var check = _database.CreateContext();
        Assert.Equal((5, 20, 30, 10, 60, 240), (
            await check.Branches.CountAsync(), await check.Staff.CountAsync(), await check.Products.CountAsync(),
            await check.ShippingZones.CountAsync(), await check.ShippingRates.CountAsync(), await check.CounterTransactions.CountAsync()));
        var hashes = await check.Staff.Select(staff => staff.PasswordHash).ToListAsync();
        Assert.All(hashes, hash => Assert.StartsWith("PBKDF2-SHA256$", hash));
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
        Assert.All(await check.Database.SqlQuery<string>($"SELECT MaskedDocumentReference AS [Value] FROM IdentityVerifications").ToListAsync(), reference => Assert.Matches("^\\*{4}.{0,4}$", reference));
        Assert.Contains(await check.CounterTransactions.Select(transaction => transaction.Status).Distinct().ToListAsync(), status => status == CounterTransactionStatus.Refunded);
    }

    [Fact]
    public async Task Demo_seed_refuses_a_database_that_has_not_been_migrated()
    {
        var unmigrated = new SqlServerTestDatabase();
        await using var db = unmigrated.CreateContext();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoDataSeeder(db, new Pbkdf2PasswordHasher(10_000)).SeedAsync("seed-test-password"));

        Assert.Contains("pending migrations", failure.Message, StringComparison.Ordinal);
        Assert.False(await db.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Shipping_rates_are_read_once_then_served_from_the_cache_but_an_empty_table_is_not_cached()
    {
        var cache = new ShippingRateCache();
        await using (var db = _database.CreateContext())
        {
            Assert.Empty(await new EfShippingRateProvider(db, cache).GetRatesAsync());
            db.ShippingZones.Add(new ShippingZoneEntity { ZoneCode = new ZoneCode("ZONE-CACHE"), Name = "Cache Zone" });
            db.ShippingRates.Add(new ShippingRate(ShipmentType.Letter, ShippingServiceLevel.Standard, new ZoneCode("ZONE-CACHE"), new Money(5m), new ParcelWeight(1m)));
            await db.SaveChangesAsync();
            Assert.Single(await new EfShippingRateProvider(db, cache).GetRatesAsync());
        }

        // The context is disposed, so a database read would fail: the cached table completes synchronously instead.
        var disposed = _database.CreateContext();
        await disposed.DisposeAsync();
        var cached = new EfShippingRateProvider(disposed, cache).GetRatesAsync();
        Assert.True(cached.IsCompletedSuccessfully);
        Assert.Single(await cached);
    }

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();
}
