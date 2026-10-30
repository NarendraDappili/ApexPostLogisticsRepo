using ApexPost.Domain.Enums;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Pricing;
using ApexPost.Infrastructure.Seed;

namespace ApexPost.Infrastructure.Tests;

public sealed class JsonShippingRateProviderTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApexPost.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
    private JsonRepositoryOptions Options => new() { DataDirectory = _directory };

    [Fact]
    public async Task Provider_loads_the_deterministic_seed_rates()
    {
        await new DemoDataSeeder(Options).SeedAsync();

        var rates = await new JsonShippingRateProvider(Options).GetRatesAsync();

        Assert.Equal(60, rates.Count);
        Assert.Contains(rates, rate => rate.ShipmentType == ShipmentType.Parcel && rate.ServiceLevel == ShippingServiceLevel.Registered && rate.DestinationZoneCode.Value == "ZONE-10");
    }

    [Fact]
    public async Task Provider_returns_no_rates_for_a_missing_configuration_file_and_propagates_cancellation()
    {
        Assert.Empty(await new JsonShippingRateProvider(Options).GetRatesAsync());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JsonShippingRateProvider(Options).GetRatesAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Provider_serves_later_calls_from_its_cache_synchronously()
    {
        await new DemoDataSeeder(Options).SeedAsync();
        var provider = new JsonShippingRateProvider(Options);
        var first = await provider.GetRatesAsync();

        var cached = provider.GetRatesAsync();

        Assert.True(cached.IsCompletedSuccessfully);
        Assert.Same(first, await cached);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
