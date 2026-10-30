using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Shipping;

/// <summary>An external courier's tracking lookup (simulated in this release).</summary>
public interface ICourierTrackingClient
{
    Task<string> GetStatusAsync(ShipmentId shipmentId, CancellationToken cancellationToken = default);
}

public sealed class CourierTrackingOptions
{
    public TimeSpan LookupTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed record ParcelStatusResult(Shipment Shipment, string CourierStatus, bool CourierLookupTimedOut);

/// <summary>
/// Combines the stored shipment with the courier's live status. The courier lookup has its own timeout so a slow
/// courier degrades the answer instead of holding the partner request open.
/// </summary>
public sealed class ParcelStatusService
{
    public const string UnavailableStatus = "Unavailable";
    private readonly IShipmentRepository _shipments;
    private readonly ICourierTrackingClient _courier;
    private readonly CourierTrackingOptions _options;

    public ParcelStatusService(IShipmentRepository shipments, ICourierTrackingClient courier, CourierTrackingOptions options)
    {
        ArgumentNullException.ThrowIfNull(shipments);
        ArgumentNullException.ThrowIfNull(courier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.LookupTimeout, TimeSpan.Zero);
        _shipments = shipments;
        _courier = courier;
        _options = options;
    }

    public async Task<ParcelStatusResult?> GetAsync(ShipmentId shipmentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentId);
        var shipment = await _shipments.FindByIdAsync(shipmentId, cancellationToken).ConfigureAwait(false);
        if (shipment is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.LookupTimeout);
        try
        {
            var status = await _courier.GetStatusAsync(shipment.Id, timeout.Token).ConfigureAwait(false);
            return new ParcelStatusResult(shipment, status, CourierLookupTimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only our timeout fired: the courier is slow, which is a degraded dependency, not a cancelled request.
            // A caller cancellation does not match this filter and propagates as cancellation.
            return new ParcelStatusResult(shipment, UnavailableStatus, CourierLookupTimedOut: true);
        }
    }
}
