using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Tracking;

/// <summary>A new tracked shipment, as the API received it; the domain checks every value.</summary>
public sealed record NewTrackedShipment(string TrackingNumber, string RecipientName, string RecipientPhone, string RecipientAddress, string City, decimal WeightKg);

/// <summary>The editable details of a tracked shipment; a replace sends all of them.</summary>
public sealed record TrackedShipmentDetails(string RecipientName, string RecipientPhone, string RecipientAddress, string City, decimal WeightKg);

/// <summary>One page of the shipment list. <see cref="Status"/> and <see cref="City"/> filter when given.</summary>
public sealed record TrackingListQuery(int Page = 1, int PageSize = TrackingListQuery.DefaultPageSize, TrackingStatus? Status = null, string? City = null)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>A tracked shipment with its latest scan (by scan time), if any.</summary>
public sealed record TrackedShipmentView(
    string TrackingNumber,
    TrackingStatus Status,
    decimal WeightKg,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddress,
    string City,
    TrackingScanView? LatestScan)
{
    public static TrackedShipmentView From(TrackedShipment shipment, TrackingScanView? latestScan = null) => new(
        shipment.TrackingNumber.Value, shipment.Status, shipment.WeightKg,
        shipment.Recipient.Name, shipment.Recipient.Phone, shipment.Recipient.Address, shipment.Recipient.City, latestScan);
}

public sealed record TrackingScanView(
    string ScanId,
    TrackingScanType ScanType,
    string Location,
    string CourierId,
    DateTimeOffset ScannedAtUtc,
    string? Notes,
    DateTimeOffset ReceivedAtUtc);
