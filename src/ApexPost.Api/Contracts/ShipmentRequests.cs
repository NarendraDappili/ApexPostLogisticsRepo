using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ApexPost.Api.Validation;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Contracts;

// Request bodies and the list query. [ApiController] validates them before an action runs and answers a failure
// with a 400 ValidationProblemDetails, keyed by the JSON field names. They are never bound to an entity: the service
// builds the domain objects, and checks the same rules again. Fields are nullable so that a missing field is reported
// as missing ([Required]) instead of arriving as "" or 0. The limits are the domain's own constants.

/// <summary>POST /api/shipments: a new shipment, which starts in Created status.</summary>
public sealed class CreateShipmentRequest
{
    /// <summary>APX- followed by eight digits, for example APX-24000001.</summary>
    [Required]
    [RegularExpression(TrackingNumberPattern, ErrorMessage = "{0} must be APX- followed by eight digits, for example APX-24000001.")]
    public string? TrackingNumber { get; init; }

    [Required]
    [StringLength(Recipient.NameMaxLength)]
    public string? RecipientName { get; init; }

    [Required]
    [PhoneNumber]
    public string? RecipientPhone { get; init; }

    [Required]
    [StringLength(Recipient.AddressMaxLength)]
    public string? RecipientAddress { get; init; }

    [Required]
    [StringLength(Recipient.CityMaxLength)]
    public string? City { get; init; }

    /// <summary>Between 0.1 and 70 kg.</summary>
    [Required]
    [WeightRange]
    public decimal? WeightKg { get; init; }

    // The brief's ^APX-\d{8}$ with ASCII digits only: .NET's \d also matches other scripts' digits. The attribute
    // requires the whole value to match, so a trailing line break fails too.
    internal const string TrackingNumberPattern = "^APX-[0-9]{8}$";

    internal NewTrackedShipment ToApplication() => new(TrackingNumber!, RecipientName!, RecipientPhone!, RecipientAddress!, City!, WeightKg!.Value);
}

/// <summary>PUT /api/shipments/{trackingNumber}: every editable detail, replacing the stored ones.</summary>
public sealed class UpdateShipmentRequest
{
    [Required]
    [StringLength(Recipient.NameMaxLength)]
    public string? RecipientName { get; init; }

    [Required]
    [PhoneNumber]
    public string? RecipientPhone { get; init; }

    [Required]
    [StringLength(Recipient.AddressMaxLength)]
    public string? RecipientAddress { get; init; }

    [Required]
    [StringLength(Recipient.CityMaxLength)]
    public string? City { get; init; }

    /// <summary>Between 0.1 and 70 kg.</summary>
    [Required]
    [WeightRange]
    public decimal? WeightKg { get; init; }

    internal TrackedShipmentDetails ToApplication() => new(RecipientName!, RecipientPhone!, RecipientAddress!, City!, WeightKg!.Value);
}

/// <summary>PATCH /api/shipments/{trackingNumber}/status: the next status, by name ("PickedUp") or number (1).</summary>
public sealed class UpdateStatusRequest
{
    [Required]
    [EnumDataType(typeof(TrackingStatus), ErrorMessage = ShipmentListQuery.UnknownStatus)]
    [JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))]
    public TrackingStatus? Status { get; init; }
}

/// <summary>GET /api/shipments?page=&amp;pageSize=&amp;status=&amp;city=</summary>
public sealed class ShipmentListQuery
{
    // The explicit names key a validation error by the query parameter the client wrote ("pageSize", not "PageSize").

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue, ErrorMessage = "{0} must be 1 or more.")]
    public int Page { get; init; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, TrackingListQuery.MaximumPageSize, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int PageSize { get; init; } = TrackingListQuery.DefaultPageSize;

    /// <summary>Only shipments in this status, by name or number.</summary>
    [FromQuery(Name = "status")]
    [EnumDataType(typeof(TrackingStatus), ErrorMessage = UnknownStatus)]
    public TrackingStatus? Status { get; init; }

    /// <summary>Only shipments to this city; case does not matter.</summary>
    [FromQuery(Name = "city")]
    [StringLength(Recipient.CityMaxLength)]
    public string? City { get; init; }

    internal const string UnknownStatus = "{0} is not a known shipment status.";

    internal TrackingListQuery ToApplication() => new(Page, PageSize, Status, City);
}

/// <summary>[Range] for the shipment weight, from the domain limits, parsed and compared in the invariant culture.</summary>
internal sealed class WeightRangeAttribute : RangeAttribute
{
    public WeightRangeAttribute()
        : base(typeof(decimal), TrackedShipment.MinimumWeightKg.ToString(System.Globalization.CultureInfo.InvariantCulture), TrackedShipment.MaximumWeightKg.ToString(System.Globalization.CultureInfo.InvariantCulture))
    {
        ParseLimitsInInvariantCulture = true;
        ConvertValueInInvariantCulture = true;
        ErrorMessage = "{0} must be between {1} and {2} kg.";
    }
}
