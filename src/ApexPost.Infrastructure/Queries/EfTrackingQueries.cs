using ApexPost.Application.Tracking;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Queries;

/// <summary>
/// The tracking API's reads (Week 4): no-tracking projections, no aggregate loaded.
/// <list type="bullet">
/// <item>A shipment view is its row plus its latest scan, read in a second statement for just the shipments on the
/// page. In one statement EF Core numbers every scan in the table (ROW_NUMBER over TrackingScans) and joins the
/// first ones: measured on 30,000 scans, a 20-row page read the whole table (560 pages) instead of seeking.</item>
/// <item>Every read uses an index: AK_TrackedShipments_TrackingNumber, IX_TrackedShipments_Status_TrackingNumber or
/// IX_TrackedShipments_City for the shipments, IX_TrackingScans_TrackingNumber_ScannedAtUtc for the scans.</item>
/// </list>
/// </summary>
public sealed class EfTrackingQueries(ApexPostDbContext db) : ITrackingQueries
{
    public async Task<PagedResult<TrackedShipmentView>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filtered = Filtered(query);
        var total = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = filtered.OrderBy(shipment => shipment.TrackingNumber).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var items = total == 0 ? [] : await WithLatestScansAsync(page, cancellationToken).ConfigureAwait(false);
        return new PagedResult<TrackedShipmentView>(items, query.Page, query.PageSize, total);
    }

    public async Task<TrackedShipmentView?> GetAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) =>
        (await WithLatestScansAsync(Shipments.Where(shipment => shipment.TrackingNumber == trackingNumber), cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    public async Task<IReadOnlyList<TrackingScanView>?> GetScanHistoryAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default)
    {
        var scans = await Scans.Where(scan => scan.TrackingNumber == trackingNumber)
            .OrderBy(scan => scan.ScannedAtUtc).ThenBy(scan => scan.ReceivedAtUtc)
            .Select(scan => new TrackingScanView(scan.Id.Value, scan.ScanType, scan.Location, scan.CourierId, scan.ScannedAtUtc, scan.Notes, scan.ReceivedAtUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        // A shipment with scans exists; only an empty answer needs the second look.
        if (scans.Count > 0) return scans;
        return await Shipments.AnyAsync(shipment => shipment.TrackingNumber == trackingNumber, cancellationToken).ConfigureAwait(false) ? scans : null;
    }

    internal IQueryable<TrackedShipment> Filtered(TrackingListQuery query)
    {
        var shipments = Shipments;
        if (query.Status is { } status) shipments = shipments.Where(shipment => shipment.Status == status);
        if (query.City is { } city) shipments = shipments.Where(shipment => shipment.Recipient.City == city);
        return shipments;
    }

    private IQueryable<TrackedShipment> Shipments => db.Set<TrackedShipment>().AsNoTracking();

    private IQueryable<TrackingScan> Scans => db.Set<TrackingScan>().AsNoTracking();

    private async Task<List<TrackedShipmentView>> WithLatestScansAsync(IQueryable<TrackedShipment> shipments, CancellationToken cancellationToken)
    {
        var rows = await shipments
            .Select(shipment => new { shipment.TrackingNumber, shipment.Status, shipment.WeightKg, shipment.Recipient.Name, shipment.Recipient.Phone, shipment.Recipient.Address, shipment.Recipient.City })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return [];

        // The newest scan of each shipment on the page: ROW_NUMBER over only their scans, found by index seeks.
        var numbers = rows.Select(row => row.TrackingNumber).ToList();
        var latest = await Scans.Where(scan => numbers.Contains(scan.TrackingNumber))
            .GroupBy(scan => scan.TrackingNumber)
            .Select(group => group.OrderByDescending(scan => scan.ScannedAtUtc).ThenByDescending(scan => scan.ReceivedAtUtc)
                .Select(scan => new { scan.TrackingNumber, View = new TrackingScanView(scan.Id.Value, scan.ScanType, scan.Location, scan.CourierId, scan.ScannedAtUtc, scan.Notes, scan.ReceivedAtUtc) })
                .First())
            .ToDictionaryAsync(scan => scan.TrackingNumber, scan => scan.View, cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row => new TrackedShipmentView(
            row.TrackingNumber.Value, row.Status, row.WeightKg, row.Name, row.Phone, row.Address, row.City,
            latest.GetValueOrDefault(row.TrackingNumber)))];
    }
}
