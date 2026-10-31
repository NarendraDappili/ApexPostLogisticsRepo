using ApexPost.Application.Repositories;
using ApexPost.Application.Shipping;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Shipping;

public sealed class ParcelStatusServiceTests
{
    [Fact]
    public async Task A_prompt_courier_answer_is_returned()
    {
        var service = CreateService(new Courier(TimeSpan.Zero));

        var status = await service.GetAsync(new("SHP-1"));

        Assert.Equal("In transit", status!.CourierStatus);
        Assert.False(status.CourierLookupTimedOut);
    }

    [Fact]
    public async Task A_slow_courier_times_out_into_a_degraded_answer_instead_of_failing()
    {
        var service = CreateService(new Courier(TimeSpan.FromSeconds(30)), timeout: TimeSpan.FromMilliseconds(50));

        var status = await service.GetAsync(new("SHP-1"));

        Assert.Equal(ParcelStatusService.UnavailableStatus, status!.CourierStatus);
        Assert.True(status.CourierLookupTimedOut);
        Assert.Equal("SHP-1", status.Shipment.Id.Value);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_reported_as_a_timeout()
    {
        var service = CreateService(new Courier(TimeSpan.FromSeconds(30)), timeout: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(new("SHP-1"), cancellation.Token));
    }

    [Fact]
    public async Task An_unknown_shipment_returns_null_without_calling_the_courier()
    {
        var courier = new Courier(TimeSpan.Zero);

        Assert.Null(await CreateService(courier).GetAsync(new("SHP-404")));
        Assert.Equal(0, courier.Calls);
    }

    private static ParcelStatusService CreateService(Courier courier, TimeSpan? timeout = null) =>
        new(new ShipmentRepo(), courier, new CourierTrackingOptions { LookupTimeout = timeout ?? TimeSpan.FromSeconds(5) });

    private sealed class Courier(TimeSpan latency) : ICourierTrackingClient
    {
        public int Calls { get; private set; }
        public async Task<string> GetStatusAsync(ShipmentId shipmentId, CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Delay(latency, cancellationToken);
            return "In transit";
        }
    }

    private sealed class ShipmentRepo : IShipmentRepository
    {
        private readonly Shipment _shipment = new(new("SHP-1"), new("BR-A"), new("ZONE-A"), new(1m), new(1m, 1m, 1m), ShipmentType.Parcel, ShippingServiceLevel.Standard);
        public Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default) => Task.FromResult(id == _shipment.Id ? _shipment : null);
        public Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Shipment>>([_shipment]);
        public void Add(Shipment shipment) { }
    }
}
