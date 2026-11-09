using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tracking;

/// <summary>No-tracking reads for the tracking API: one SQL statement per answer, no aggregate loaded.</summary>
public interface ITrackingQueries
{
    /// <summary>One page ordered by tracking number, with the total count of matching shipments.</summary>
    Task<PagedResult<TrackedShipmentView>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken = default);

    Task<TrackedShipmentView?> GetAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>The shipment's scans, oldest first; null when the shipment does not exist.</summary>
    Task<IReadOnlyList<TrackingScanView>?> GetScanHistoryAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default);
}
