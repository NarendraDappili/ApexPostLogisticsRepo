using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

/// <summary>Loads and stages tracked shipments for the tracking API's commands; reads go through ITrackingQueries.</summary>
public interface ITrackedShipmentRepository
{
    /// <summary>The shipment, tracked so a change to it is saved by the unit of work; null when it does not exist.</summary>
    Task<TrackedShipment?> FindAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default);

    void Add(TrackedShipment shipment);

    void Remove(TrackedShipment shipment);

    /// <summary>Which of these scan IDs are already stored, and for which shipment. Unknown IDs are left out.</summary>
    Task<IReadOnlyDictionary<TrackingScanId, TrackingNumber>> FindScanOwnersAsync(IReadOnlyCollection<TrackingScanId> scanIds, CancellationToken cancellationToken = default);

    /// <summary>Stages a scan that <see cref="TrackedShipment.RecordScan"/> accepted.</summary>
    void AddScan(TrackingScan scan);
}
