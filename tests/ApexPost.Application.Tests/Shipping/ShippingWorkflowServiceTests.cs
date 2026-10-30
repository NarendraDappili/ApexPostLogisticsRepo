using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Application.Shipping;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Shipping;

public sealed class ShippingWorkflowServiceTests
{
    [Theory]
    [InlineData(ShipmentType.Letter, ShippingServiceLevel.Standard, "ZONE-A")]
    [InlineData(ShipmentType.Letter, ShippingServiceLevel.Express, "ZONE-B")]
    [InlineData(ShipmentType.Letter, ShippingServiceLevel.Registered, "ZONE-C")]
    [InlineData(ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A")]
    [InlineData(ShipmentType.Parcel, ShippingServiceLevel.Express, "ZONE-B")]
    [InlineData(ShipmentType.Parcel, ShippingServiceLevel.Registered, "ZONE-C")]
    public async Task Create_prices_and_persists_each_shipment_type_and_service(ShipmentType shipmentType, ShippingServiceLevel serviceLevel, string zone)
    {
        var shipments = new InMemoryShipmentRepository();
        var service = CreateService(shipments, [Rate(shipmentType, serviceLevel, zone, 17m, 5m)]);

        var result = await service.CreateAsync(Request("SHIP-1", shipmentType, serviceLevel, zone, 5m));

        Assert.Equal(17m, result.Price.Amount);
        Assert.Equal(shipmentType, result.ShipmentType);
        Assert.Equal(serviceLevel, result.ServiceLevel);
        Assert.NotNull(await shipments.FindByIdAsync(new("SHIP-1")));
    }

    [Fact]
    public async Task Create_links_the_shipment_to_the_transaction_that_will_bill_it()
    {
        var shipments = new InMemoryShipmentRepository();
        var service = CreateService(shipments, [Rate(ShipmentType.Parcel, ShippingServiceLevel.Express, "ZONE-A", 17m, 5m)]);

        await service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Express, "ZONE-A", 1m) with { TransactionId = "tx-9" });

        Assert.Equal("TX-9", (await shipments.FindByIdAsync(new("SHIP-1")))!.TransactionId?.Value);
    }

    [Fact]
    public async Task Create_rejects_weight_above_the_configured_service_limit_without_persisting()
    {
        var shipments = new InMemoryShipmentRepository();
        var service = CreateService(shipments, [Rate(ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 10m, 5m)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 5.01m)));
        Assert.Null(await shipments.FindByIdAsync(new("SHIP-1")));
    }

    [Fact]
    public async Task Create_rejects_an_unsupported_destination_or_service_rate()
    {
        var shipments = new InMemoryShipmentRepository();
        var service = CreateService(shipments, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-UNKNOWN", 1m)));
        Assert.Null(await shipments.FindByIdAsync(new("SHIP-1")));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(-1, 1, 1, 1)]
    public async Task Create_rejects_invalid_weight_or_dimensions(decimal weight, decimal length, decimal width, decimal height)
    {
        var service = CreateService(new InMemoryShipmentRepository(), [Rate(ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 10m, 5m)]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", weight, length, width, height)));
    }

    [Fact]
    public async Task Create_propagates_persistence_failures_after_pricing()
    {
        var shipments = new InMemoryShipmentRepository { SaveException = new IOException("Disk unavailable") };
        var service = CreateService(shipments, [Rate(ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 10m, 5m)]);

        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 1m)));
    }

    [Fact]
    public async Task Create_propagates_cancellation_before_accessing_repositories()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var service = CreateService(new InMemoryShipmentRepository(), [Rate(ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 10m, 5m)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 1m), cancellation.Token));
    }

    [Fact]
    public async Task Missing_origin_or_shipment_is_distinctly_reported()
    {
        var missingBranch = new InMemoryBranchRepository();
        var service = new ShippingWorkflowService(missingBranch, new InMemoryShipmentRepository(), new InMemoryRateProvider([]));

        await Assert.ThrowsAsync<OriginBranchNotFoundException>(() => service.CreateAsync(Request("SHIP-1", ShipmentType.Parcel, ShippingServiceLevel.Standard, "ZONE-A", 1m)));
        await Assert.ThrowsAsync<ShipmentNotFoundException>(() => service.GetAsync(new("NOT-THERE")));
    }

    private static ShippingWorkflowService CreateService(InMemoryShipmentRepository shipments, IReadOnlyCollection<ShippingRate> rates) =>
        new(new InMemoryBranchRepository(new Branch(new("BR-A"), "Branch A")), shipments, new InMemoryRateProvider(rates));

    private static ShipmentCreationRequest Request(string id, ShipmentType type, ShippingServiceLevel service, string zone, decimal weight, decimal length = 10m, decimal width = 10m, decimal height = 1m) =>
        new(id, "BR-A", zone, type, service, weight, length, width, height);

    private static ShippingRate Rate(ShipmentType type, ShippingServiceLevel service, string zone, decimal price, decimal maximumWeight) =>
        new(type, service, new ZoneCode(zone), new Money(price), new ParcelWeight(maximumWeight));

    private sealed class InMemoryRateProvider(IReadOnlyCollection<ShippingRate> rates) : IShippingRateProvider
    {
        public ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(rates);
        }
    }

    private sealed class InMemoryBranchRepository(params Branch[] branches) : IBranchRepository
    {
        private readonly Dictionary<string, Branch> _branches = branches.ToDictionary(branch => branch.Code.Value);
        public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _branches.TryGetValue(code.Value, out var branch);
            return Task.FromResult(branch);
        }
        public Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Branch>>(_branches.Values.ToArray());
        public Task SaveAsync(Branch branch, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryShipmentRepository : IShipmentRepository
    {
        private readonly Dictionary<string, Shipment> _shipments = [];
        public Exception? SaveException { get; init; }
        public Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _shipments.TryGetValue(id.Value, out var shipment);
            return Task.FromResult(shipment);
        }
        public Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Shipment>>(_shipments.Values.Where(shipment => shipment.OriginBranchCode == branchCode).ToArray());
        public Task SaveAsync(Shipment shipment, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SaveException is not null) throw SaveException;
            _shipments[shipment.Id.Value] = shipment;
            return Task.CompletedTask;
        }
    }
}
