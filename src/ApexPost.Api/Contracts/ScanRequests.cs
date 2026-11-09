using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ApexPost.Api.Validation;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Contracts;

// A malformed scan fails the whole request with a 400 whose keys name it, for example scans[2].scannedAtUtc, so the
// device can tell which scan to fix. A well-formed scan that the shipment's status rules refuse is reported per scan
// in the 200 response instead. The XML comments are written for API clients (see ShipmentRequests.cs).

/// <summary>Scans a courier's device recorded offline for one shipment, sent together.</summary>
public sealed class ScanUploadRequest : IValidatableObject
{
    /// <summary>The courier's offline batches hold 20 scans; 100 leaves room without allowing unbounded bodies.</summary>
    public const int MaximumScans = 100;

    /// <summary>One to 100 scans, each with a different scan ID. Their order does not matter: the oldest is applied first.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(MaximumScans)]
    public IReadOnlyList<ScanUploadItem>? Scans { get; init; }

    /// <summary>Custom rule: one upload cannot carry the same scan twice; a retry sends it again in a new request.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var repeated = (Scans ?? [])
            .Where(scan => scan?.ScanId is not null)
            .GroupBy(scan => scan.ScanId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (repeated.Count > 0)
        {
            // MVC keys an object-level result by the member name exactly as given, so give the JSON name.
            yield return new ValidationResult($"Each scan ID can appear only once in an upload; repeated: {string.Join(", ", repeated)}.", ["scans"]);
        }
    }

    internal IReadOnlyList<UploadedScan> ToApplication() => [.. Scans!.Select(scan => scan.ToApplication())];
}

/// <summary>One scan exactly as the courier's device recorded it.</summary>
public sealed class ScanUploadItem
{
    /// <summary>The ID the device gave the scan: scan:: and a GUID. Sending the same ID again never stores the scan twice.</summary>
    /// <example>scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10</example>
    [Required]
    [RegularExpression(ScanIdPattern, ErrorMessage = "{0} must be scan:: followed by a GUID, for example scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10.")]
    public string? ScanId { get; init; }

    /// <summary>What happened: Pickup, InTransit, Delivered or Failed. Its number (0 to 3) is accepted too.</summary>
    /// <example>Pickup</example>
    [Required]
    [EnumDataType(typeof(TrackingScanType), ErrorMessage = "{0} is not a known scan type.")]
    [JsonConverter(typeof(JsonStringEnumConverter<TrackingScanType>))]
    public TrackingScanType? ScanType { get; init; }

    /// <summary>Where the parcel was scanned, up to 100 characters.</summary>
    /// <example>Portside depot</example>
    [Required]
    [StringLength(TrackingScan.LocationMaxLength)]
    public string? Location { get; init; }

    /// <summary>The courier who scanned it, up to 32 characters.</summary>
    /// <example>CR-1042</example>
    [Required]
    [StringLength(TrackingScan.CourierIdMaxLength)]
    public string? CourierId { get; init; }

    /// <summary>When the courier scanned the parcel, by the device clock. At most 5 minutes ahead of the server clock.</summary>
    /// <example>2026-11-02T08:30:00Z</example>
    [Required]
    [NotInFuture]
    public DateTimeOffset? ScannedAtUtc { get; init; }

    /// <summary>Optional notes from the courier, up to 500 characters.</summary>
    /// <example>Collected from reception.</example>
    [StringLength(TrackingScan.NotesMaxLength)]
    public string? Notes { get; init; }

    internal const string ScanIdPattern = "^scan::[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    internal UploadedScan ToApplication() => new(ScanId!, ScanType!.Value, Location!, CourierId!, ScannedAtUtc!.Value, Notes);
}
