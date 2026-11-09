using System.ComponentModel;
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
// The XML comments are written for API clients: they are the descriptions and examples in the OpenAPI document.

/// <summary>A new shipment, which starts in Created status.</summary>
public sealed class CreateShipmentRequest
{
    /// <summary>APX- followed by eight digits. It must not be in use already.</summary>
    /// <example>APX-24000001</example>
    [Required]
    [RegularExpression(TrackingNumberPattern, ErrorMessage = "{0} must be APX- followed by eight digits, for example APX-24000001.")]
    public string? TrackingNumber { get; init; }

    /// <summary>The recipient's full name, up to 100 characters.</summary>
    /// <example>Ama Mensah</example>
    [Required]
    [StringLength(Recipient.NameMaxLength)]
    public string? RecipientName { get; init; }

    /// <summary>A number a courier can dial: 7 to 15 digits, with an optional leading +, spaces, hyphens and brackets.</summary>
    /// <example>+44 20 7946 0000</example>
    [Required]
    [PhoneNumber]
    public string? RecipientPhone { get; init; }

    /// <summary>The delivery address without the city, up to 200 characters.</summary>
    /// <example>12 Harbour Road</example>
    [Required]
    [StringLength(Recipient.AddressMaxLength)]
    public string? RecipientAddress { get; init; }

    /// <summary>The delivery city, up to 80 characters.</summary>
    /// <example>Portside</example>
    [Required]
    [StringLength(Recipient.CityMaxLength)]
    public string? City { get; init; }

    /// <summary>The parcel's weight in kilograms, from 0.1 to 70.</summary>
    /// <example>2.5</example>
    [Required]
    [WeightRange]
    public decimal? WeightKg { get; init; }

    // The brief's ^APX-\d{8}$ with ASCII digits only: .NET's \d also matches other scripts' digits. The attribute
    // requires the whole value to match, so a trailing line break fails too.
    internal const string TrackingNumberPattern = "^APX-[0-9]{8}$";

    internal NewTrackedShipment ToApplication() => new(TrackingNumber!, RecipientName!, RecipientPhone!, RecipientAddress!, City!, WeightKg!.Value);
}

/// <summary>Every editable detail of a shipment. Together they replace the stored ones.</summary>
public sealed class UpdateShipmentRequest
{
    /// <summary>The recipient's full name, up to 100 characters.</summary>
    /// <example>Ama Mensah</example>
    [Required]
    [StringLength(Recipient.NameMaxLength)]
    public string? RecipientName { get; init; }

    /// <summary>A number a courier can dial: 7 to 15 digits, with an optional leading +, spaces, hyphens and brackets.</summary>
    /// <example>+44 20 7946 0000</example>
    [Required]
    [PhoneNumber]
    public string? RecipientPhone { get; init; }

    /// <summary>The delivery address without the city, up to 200 characters.</summary>
    /// <example>14 Harbour Road</example>
    [Required]
    [StringLength(Recipient.AddressMaxLength)]
    public string? RecipientAddress { get; init; }

    /// <summary>The delivery city, up to 80 characters.</summary>
    /// <example>Portside</example>
    [Required]
    [StringLength(Recipient.CityMaxLength)]
    public string? City { get; init; }

    /// <summary>The parcel's weight in kilograms, from 0.1 to 70.</summary>
    /// <example>3.2</example>
    [Required]
    [WeightRange]
    public decimal? WeightKg { get; init; }

    internal TrackedShipmentDetails ToApplication() => new(RecipientName!, RecipientPhone!, RecipientAddress!, City!, WeightKg!.Value);
}

/// <summary>The status to move a shipment to.</summary>
public sealed class UpdateStatusRequest
{
    /// <summary>The next status: PickedUp, InTransit, OutForDelivery, Delivered or Failed. Its number (1 to 5) is accepted too.</summary>
    /// <example>PickedUp</example>
    [Required]
    [EnumDataType(typeof(TrackingStatus), ErrorMessage = ShipmentListQuery.UnknownStatus)]
    [JsonConverter(typeof(JsonStringEnumConverter<TrackingStatus>))]
    public TrackingStatus? Status { get; init; }
}

/// <summary>The page of shipments to return, and the optional filters.</summary>
public sealed class ShipmentListQuery
{
    // The explicit names key a validation error by the query parameter the client wrote ("pageSize", not "PageSize").

    /// <summary>The page to return, from 1.</summary>
    [Description("The page to return, from 1.")]
    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue, ErrorMessage = "{0} must be 1 or more.")]
    public int Page { get; init; } = 1;

    /// <summary>Shipments per page: 20 by default, at most 50.</summary>
    [Description("Shipments per page: 20 by default, at most 50.")]
    [FromQuery(Name = "pageSize")]
    [Range(1, TrackingListQuery.MaximumPageSize, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int PageSize { get; init; } = TrackingListQuery.DefaultPageSize;

    /// <summary>Only shipments in this status: Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed (or its number, 0 to 5).</summary>
    [Description("Only shipments in this status: Created, PickedUp, InTransit, OutForDelivery, Delivered or Failed (or its number, 0 to 5).")]
    [FromQuery(Name = "status")]
    [EnumDataType(typeof(TrackingStatus), ErrorMessage = UnknownStatus)]
    public TrackingStatus? Status { get; init; }

    /// <summary>Only shipments to this city; case does not matter.</summary>
    [Description("Only shipments to this city; case does not matter.")]
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
