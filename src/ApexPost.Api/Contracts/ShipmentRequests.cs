using System.Text.Json.Serialization;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Contracts;

// Request bodies and the list query. They are bound by [ApiController] and never bound to an entity; the service
// builds the domain objects. A non-nullable string is required by model binding (nullable reference types are on).

/// <summary>POST /api/shipments: a new shipment, which starts in Created status.</summary>
public sealed class CreateShipmentRequest
{
    public string TrackingNumber { get; init; } = string.Empty;

    public string RecipientName { get; init; } = string.Empty;

    public string RecipientPhone { get; init; } = string.Empty;

    public string RecipientAddress { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public decimal WeightKg { get; init; }

    internal NewTrackedShipment ToApplication() => new(TrackingNumber, RecipientName, RecipientPhone, RecipientAddress, City, WeightKg);
}

/// <summary>PUT /api/shipments/{trackingNumber}: every editable detail, replacing the stored ones.</summary>
public sealed class UpdateShipmentRequest
{
    public string RecipientName { get; init; } = string.Empty;

    public string RecipientPhone { get; init; } = string.Empty;

    public string RecipientAddress { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public decimal WeightKg { get; init; }

    internal TrackedShipmentDetails ToApplication() => new(RecipientName, RecipientPhone, RecipientAddress, City, WeightKg);
}

/// <summary>PATCH /api/shipments/{trackingNumber}/status: the next status, by name ("PickedUp") or number (1).</summary>
public sealed class UpdateStatusRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))]
    public TrackingStatus Status { get; init; }
}

/// <summary>GET /api/shipments?page=&amp;pageSize=&amp;status=&amp;city=</summary>
public sealed class ShipmentListQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = TrackingListQuery.DefaultPageSize;

    /// <summary>Only shipments in this status, by name or number.</summary>
    public TrackingStatus? Status { get; init; }

    /// <summary>Only shipments to this city; case does not matter.</summary>
    public string? City { get; init; }

    internal TrackingListQuery ToApplication() => new(Page, PageSize, Status, City);
}
