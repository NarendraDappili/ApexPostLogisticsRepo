using ApexPost.Application.Tracking;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Contracts;

// Response bodies. They are built from the service's read models, never from entities, so the API contract changes
// only when these classes change. Enums are written as numbers (the System.Text.Json default).
// The XML comments are written for API clients: they are the descriptions in the OpenAPI document.

/// <summary>A shipment, with its recipient fields side by side.</summary>
/// <param name="TrackingNumber">APX- followed by eight digits.</param>
/// <param name="Status">The status as a number: 0 Created, 1 PickedUp, 2 InTransit, 3 OutForDelivery, 4 Delivered, 5 Failed.</param>
/// <param name="WeightKg">The parcel's weight in kilograms.</param>
/// <param name="RecipientName">The recipient's full name.</param>
/// <param name="RecipientPhone">The recipient's phone number, as given.</param>
/// <param name="RecipientAddress">The delivery address without the city.</param>
/// <param name="City">The delivery city.</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    TrackingStatus Status,
    decimal WeightKg,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddress,
    string City)
{
    internal static ShipmentResponse From(TrackedShipmentView view) =>
        new(view.TrackingNumber, view.Status, Weight(view.WeightKg), view.RecipientName, view.RecipientPhone, view.RecipientAddress, view.City);

    // The column keeps three decimals, so a stored 69.5 reads back as 69.500. Dropping trailing zeros makes a create
    // and a later read of the same shipment return the same JSON.
    private static decimal Weight(decimal kilograms) => kilograms / 1.000000000000000000000000000000000m;
}

/// <summary>One scan in a shipment's history.</summary>
/// <param name="ScanId">The ID the courier's device gave the scan.</param>
/// <param name="ScanType">What happened, as a number: 0 Pickup, 1 InTransit, 2 Delivered, 3 Failed.</param>
/// <param name="Location">Where the parcel was scanned.</param>
/// <param name="CourierId">The courier who scanned it.</param>
/// <param name="ScannedAtUtc">When the courier scanned the parcel, by the device clock.</param>
/// <param name="Notes">The courier's notes, if any.</param>
/// <param name="ReceivedAtUtc">When the server stored the scan.</param>
public sealed record ScanResponse(
    string ScanId,
    TrackingScanType ScanType,
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
/// <param name="ShipmentStatus">The shipment's status after the upload, as a number (see ShipmentResponse.status).</param>
public sealed record ScanUploadResponse(int Accepted, int DuplicatesSkipped, int Rejected, IReadOnlyList<ScanRejectionResponse> Rejections, TrackingStatus ShipmentStatus)
{
    internal static ScanUploadResponse From(ScanUploadResult result) =>
        new(result.Accepted, result.DuplicatesSkipped, result.Rejected.Count, [.. result.Rejected.Select(rejection => new ScanRejectionResponse(rejection.ScanId, rejection.Reason))], result.ShipmentStatus);
}

/// <summary>A scan that was not stored, and why.</summary>
/// <param name="ScanId">The rejected scan's ID.</param>
/// <param name="Reason">Why it was rejected, for example a status the shipment cannot move to.</param>
public sealed record ScanRejectionResponse(string ScanId, string Reason);

/// <summary>One page of a list, with what a client needs to fetch the next one.</summary>
/// <typeparam name="T">The type of the listed items.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">This page's number, from 1.</param>
/// <param name="PageSize">The most items a page holds.</param>
/// <param name="TotalCount">Items on all pages together.</param>
/// <param name="TotalPages">The number of pages.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

internal static class PagedResponse
{
    public static PagedResponse<TResponse> From<TView, TResponse>(PagedResult<TView> page, Func<TView, TResponse> map) =>
        new([.. page.Items.Select(map)], page.Page, page.PageSize, page.TotalCount, page.TotalPages);
}
