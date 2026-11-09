using System.Text.Json.Serialization;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Contracts.V2;

// Version 2 response bodies. What changed from v1 (VERSIONING.md): the recipient fields are one nested object, the
// shipment carries a summary of its latest scan, and every enum is written by name. Requests are the same in both
// versions. Like v1's, these are built from the service's read models, never from entities.

/// <summary>A shipment, who it is for, and where it was last scanned.</summary>
/// <param name="TrackingNumber">APX- followed by eight digits.</param>
/// <param name="Status">The status: Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed.</param>
/// <param name="WeightKg">The parcel's weight in kilograms.</param>
/// <param name="Recipient">Who the parcel is for and where it goes.</param>
/// <param name="LatestScan">The most recent scan by scan time; null until the parcel is first scanned.</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))] TrackingStatus Status,
    decimal WeightKg,
    RecipientResponse Recipient,
    LatestScanResponse? LatestScan)
{
    internal static ShipmentResponse From(TrackedShipmentView view) => new(
        view.TrackingNumber,
        view.Status,
        ResponseValues.Weight(view.WeightKg),
        new RecipientResponse(view.RecipientName, view.RecipientPhone, view.RecipientAddress, view.City),
        view.LatestScan is { } scan ? new LatestScanResponse(scan.ScanType, scan.Location, scan.ScannedAtUtc) : null);
}

/// <summary>The recipient of a shipment.</summary>
/// <param name="Name">The recipient's full name.</param>
/// <param name="Phone">The recipient's phone number, as given.</param>
/// <param name="Address">The delivery address without the city.</param>
/// <param name="City">The delivery city.</param>
public sealed record RecipientResponse(string Name, string Phone, string Address, string City);

/// <summary>A summary of a shipment's most recent scan; the full scan is in the scan history.</summary>
/// <param name="ScanType">What happened: Pickup, InTransit, Delivered or Failed.</param>
/// <param name="Location">Where the parcel was scanned.</param>
/// <param name="ScannedAtUtc">When the courier scanned the parcel, by the device clock.</param>
public sealed record LatestScanResponse(
    [property: JsonConverter(typeof(JsonStringEnumConverter<TrackingScanType>))] TrackingScanType ScanType,
    string Location,
    DateTimeOffset ScannedAtUtc);

/// <summary>One scan in a shipment's history.</summary>
/// <param name="ScanId">The ID the courier's device gave the scan.</param>
/// <param name="ScanType">What happened: Pickup, InTransit, Delivered or Failed.</param>
/// <param name="Location">Where the parcel was scanned.</param>
/// <param name="CourierId">The courier who scanned it.</param>
/// <param name="ScannedAtUtc">When the courier scanned the parcel, by the device clock.</param>
/// <param name="Notes">The courier's notes, if any.</param>
/// <param name="ReceivedAtUtc">When the server stored the scan.</param>
public sealed record ScanResponse(
    string ScanId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TrackingScanType>))] TrackingScanType ScanType,
    string Location,
    string CourierId,
    DateTimeOffset ScannedAtUtc,
    string? Notes,
    DateTimeOffset ReceivedAtUtc)
{
    internal static ScanResponse From(TrackingScanView view) =>
        new(view.ScanId, view.ScanType, view.Location, view.CourierId, view.ScannedAtUtc, view.Notes, view.ReceivedAtUtc);
}

/// <summary>
/// What happened to an upload's scans. Accepted and skipped scans are both stored, so the device marks every scan
/// synced except the ones listed in rejections.
/// </summary>
/// <param name="Accepted">Scans stored by this upload.</param>
/// <param name="DuplicatesSkipped">Scans already stored by an earlier upload, or repeated in this one; nothing was stored twice.</param>
/// <param name="Rejected">Scans not stored; each is listed in rejections.</param>
/// <param name="Rejections">Each rejected scan with the reason.</param>
/// <param name="ShipmentStatus">The shipment's status after the upload: Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed.</param>
public sealed record ScanUploadResponse(
    int Accepted,
    int DuplicatesSkipped,
    int Rejected,
    IReadOnlyList<ScanRejectionResponse> Rejections,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))] TrackingStatus ShipmentStatus)
{
    internal static ScanUploadResponse From(ScanUploadResult result) =>
        new(result.Accepted, result.DuplicatesSkipped, result.Rejected.Count, [.. result.Rejected.Select(rejection => new ScanRejectionResponse(rejection.ScanId, rejection.Reason))], result.ShipmentStatus);
}
