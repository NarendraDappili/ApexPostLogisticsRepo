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
}
