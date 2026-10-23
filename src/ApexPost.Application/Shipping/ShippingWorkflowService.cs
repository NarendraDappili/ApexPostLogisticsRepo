using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Shipping;

/// <summary>Validates, prices, and persists shipments using configured rates.</summary>
public sealed class ShippingWorkflowService
{
    private readonly IBranchRepository _branches;
    private readonly IShipmentRepository _shipments;
    private readonly IShippingRateProvider _rates;

    public ShippingWorkflowService(IBranchRepository branches, IShipmentRepository shipments, IShippingRateProvider rates)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(shipments);
        ArgumentNullException.ThrowIfNull(rates);
        _branches = branches;
        _shipments = shipments;
        _rates = rates;
    }

    public async Task<ShipmentCreationResult> CreateAsync(ShipmentCreationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(request.ShipmentType)) throw new ArgumentOutOfRangeException(nameof(request.ShipmentType));
        if (!Enum.IsDefined(request.ServiceLevel)) throw new ArgumentOutOfRangeException(nameof(request.ServiceLevel));

        var shipment = CreateShipment(request);
        var branch = await _branches.FindByCodeAsync(shipment.OriginBranchCode, cancellationToken).ConfigureAwait(false)
            ?? throw new OriginBranchNotFoundException("The shipment origin branch was not found.");
        if (!branch.CanAcceptTransactions) throw new InvalidOperationException("The shipment origin branch is closed.");
        if (await _shipments.FindByIdAsync(shipment.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("A shipment with this identifier already exists.");
        }

        var rates = await _rates.GetRatesAsync(cancellationToken).ConfigureAwait(false);
        var price = ShippingPriceCalculator.Calculate(new ShippingPricingRequest(shipment, request.ShipmentType, request.ServiceLevel), rates);
        await _shipments.SaveAsync(shipment, cancellationToken).ConfigureAwait(false);
        return new ShipmentCreationResult(shipment, request.ShipmentType, request.ServiceLevel, price);
    }

    public async Task<Shipment> GetAsync(ShipmentId shipmentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentId);
        return await _shipments.FindByIdAsync(shipmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ShipmentNotFoundException("The shipment was not found.");
    }

    private static Shipment CreateShipment(ShipmentCreationRequest request) => new(
        new ShipmentId(request.ShipmentId),
        new BranchCode(request.OriginBranchCode),
        new ZoneCode(request.DestinationZoneCode),
        new ParcelWeight(request.WeightKilograms),
        new ParcelDimensions(request.LengthCentimetres, request.WidthCentimetres, request.HeightCentimetres));
}
