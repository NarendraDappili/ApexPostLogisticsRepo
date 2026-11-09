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

    // One statement, a seek per ID on the unique AK_TrackingScans_ScanId; nothing is loaded or tracked.
    public async Task<IReadOnlyDictionary<TrackingScanId, TrackingNumber>> FindScanOwnersAsync(IReadOnlyCollection<TrackingScanId> scanIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scanIds);
        if (scanIds.Count == 0) return new Dictionary<TrackingScanId, TrackingNumber>();

        var ids = scanIds.Distinct().ToList();
        return await db.Set<TrackingScan>().AsNoTracking()
            .Where(scan => ids.Contains(scan.Id))
            .Select(scan => new { scan.Id, scan.TrackingNumber })
            .ToDictionaryAsync(scan => scan.Id, scan => scan.TrackingNumber, cancellationToken).ConfigureAwait(false);
    }

    public void AddScan(TrackingScan scan) => db.Set<TrackingScan>().Add(scan);
}
