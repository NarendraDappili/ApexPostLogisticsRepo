using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Application.Shipping;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Shipping;

/// <summary>The counter's acceptance rules a shipment must pass before it is priced and saved, and the rate table's own checks.</summary>
public sealed class ShipmentAcceptanceRuleTests
{
    [Fact]
    public async Task A_closed_branch_accepts_no_shipment_and_nothing_is_saved()
    {
        var branch = new Branch(new("BR-A"), "Branch A");
        branch.Close();
        var shipments = new Shipments();
        var unitOfWork = new SpyUnitOfWork();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(branch, shipments, unitOfWork).CreateAsync(Request("SHIP-1")));

        Assert.Equal("The shipment origin branch is closed.", refused.Message);
        Assert.Empty(shipments.Saved);
        Assert.Equal(0, unitOfWork.Attempts);
    }

    [Fact]
    public async Task A_shipment_id_already_in_use_is_refused_before_pricing_and_the_first_shipment_is_kept()
    {
        var shipments = new Shipments();
        var unitOfWork = new SpyUnitOfWork();
        var service = Service(new Branch(new("BR-A"), "Branch A"), shipments, unitOfWork);
        var first = await service.CreateAsync(Request("ship-1", weight: 1m));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Request(" SHIP-1 ", weight: 2m)));

        Assert.Equal("A shipment with this identifier already exists.", refused.Message);
        Assert.Equal(1m, Assert.Single(shipments.Saved).Weight.Kilograms);
        Assert.Equal(first.Shipment, shipments.Saved.Single());
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task A_found_shipment_is_returned_by_its_normalized_id()
    {
        var shipments = new Shipments();
        var service = Service(new Branch(new("BR-A"), "Branch A"), shipments, new SpyUnitOfWork());
        await service.CreateAsync(Request("SHIP-7"));

        Assert.Equal("SHIP-7", (await service.GetAsync(new ShipmentId(" ship-7 "))).Id.Value);
    }

    [Fact]
    public void A_rate_must_name_a_known_shipment_type_service_level_and_zone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShippingRate((ShipmentType)99, ShippingServiceLevel.Standard, new ZoneCode("Z"), new Money(1m), new ParcelWeight(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShippingRate(ShipmentType.Parcel, (ShippingServiceLevel)99, new ZoneCode("Z"), new Money(1m), new ParcelWeight(1m)));
        Assert.Throws<ArgumentNullException>(() => new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, null!, new Money(1m), new ParcelWeight(1m)));
    }

    private static ShippingWorkflowService Service(Branch branch, Shipments shipments, SpyUnitOfWork unitOfWork) =>
        new(new Branches(branch), shipments, new Rates([new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, new ZoneCode("ZONE-A"), new Money(9m), new ParcelWeight(5m))]), unitOfWork);

    private static ShipmentCreationRequest Request(string id, decimal weight = 1m) =>
        new(id, "BR-A", "ZONE-A", ShipmentType.Parcel, ShippingServiceLevel.Standard, weight, 10m, 10m, 1m);

    private sealed class Rates(IReadOnlyCollection<ShippingRate> rates) : IShippingRateProvider
    {
        public ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(rates);
    }

    private sealed class Branches(Branch branch) : IBranchRepository
    {
        public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default) => Task.FromResult(code == branch.Code ? branch : null);
        public Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Branch>>([branch]);
        public void Add(Branch added) { }
        public void Update(Branch updated) { }
    }

    private sealed class Shipments : IShipmentRepository
    {
        public List<Shipment> Saved { get; } = [];
        public Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default) => Task.FromResult(Saved.SingleOrDefault(shipment => shipment.Id == id));
        public Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Shipment>>([.. Saved.Where(shipment => shipment.OriginBranchCode == branchCode)]);
        public void Add(Shipment shipment) => Saved.Add(shipment);
    }
}
