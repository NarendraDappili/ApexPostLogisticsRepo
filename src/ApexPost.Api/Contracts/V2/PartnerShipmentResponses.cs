using System.Text.Json.Serialization;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Contracts.V2;

// What a partner company sees of a shipment: enough to follow it, and nothing about the recipient but the city. The
// recipient's name, phone and address, and the scan locations (a delivery scan's location is the delivery address),
// stay with the customer portal and the couriers.

/// <summary>A shipment as a partner sees it: where it is in its journey, without the recipient's personal details.</summary>
/// <param name="TrackingNumber">APX- followed by eight digits.</param>
/// <param name="Status">The status: Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed.</param>
/// <param name="City">The delivery city.</param>
/// <param name="LatestScan">What happened at the most recent scan, and when; null until the parcel is first scanned.</param>
public sealed record PartnerShipmentResponse(
    string TrackingNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))] TrackingStatus Status,
    string City,
    PartnerScanResponse? LatestScan)
{
    internal static PartnerShipmentResponse From(TrackedShipmentView view) => new(
        view.TrackingNumber,
        view.Status,
        view.City,
        view.LatestScan is { } scan ? new PartnerScanResponse(scan.ScanType, scan.ScannedAtUtc) : null);
}

/// <summary>A scan as a partner sees it: what happened and when, without where.</summary>
/// <param name="ScanType">What happened: Pickup, InTransit, Delivered or Failed.</param>
/// <param name="ScannedAtUtc">When the courier scanned the parcel, by the device clock.</param>
public sealed record PartnerScanResponse(
    [property: JsonConverter(typeof(JsonStringEnumConverter<TrackingScanType>))] TrackingScanType ScanType,
    DateTimeOffset ScannedAtUtc);
