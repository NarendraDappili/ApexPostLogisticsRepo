using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Repositories;

/// <summary>
/// The tracking API's command side (Week 4). Like the Week 3 repositories it only stages changes in the request's
/// scoped context; <see cref="EfUnitOfWork"/> commits them.
/// </summary>
public sealed class EfTrackedShipmentRepository(ApexPostDbContext db) : ITrackedShipmentRepository
{
    // Tracked: the caller changes the shipment and the unit of work saves it against the version it read.
    public Task<TrackedShipment?> FindAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) =>
        db.Set<TrackedShipment>().SingleOrDefaultAsync(shipment => shipment.TrackingNumber == trackingNumber, cancellationToken);

    public void Add(TrackedShipment shipment) => db.Set<TrackedShipment>().Add(shipment);

    public void Remove(TrackedShipment shipment) => db.Set<TrackedShipment>().Remove(shipment);
}
