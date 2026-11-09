using ApexPost.Application.Tracking;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Contracts;

// Response bodies. They are built from the service's read models, never from entities, so the API contract changes
// only when these classes change. Enums are written as numbers (the System.Text.Json default).

/// <summary>A shipment with its recipient fields side by side.</summary>
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

/// <summary>One page of a list, with what a client needs to fetch the next one.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

internal static class PagedResponse
{
    public static PagedResponse<TResponse> From<TView, TResponse>(PagedResult<TView> page, Func<TView, TResponse> map) =>
        new([.. page.Items.Select(map)], page.Page, page.PageSize, page.TotalCount, page.TotalPages);
}
