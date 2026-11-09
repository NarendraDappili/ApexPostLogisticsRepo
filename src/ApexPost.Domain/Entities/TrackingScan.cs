using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

/// <summary>
/// One courier scan the server accepted for a tracked shipment. It is created only by
/// <see cref="TrackedShipment.RecordScan"/>, which applies the status rules, and never changes afterwards.
/// </summary>
public sealed class TrackingScan
{
    public const int LocationMaxLength = 100;
    public const int CourierIdMaxLength = 32;
    public const int NotesMaxLength = 500;

    internal TrackingScan(TrackingScanId id, TrackingNumber trackingNumber, TrackingScanType scanType, string location, string courierId, DateTimeOffset scannedAtUtc, string? notes, DateTimeOffset receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(trackingNumber);
        if (!Enum.IsDefined(scanType)) throw new ArgumentOutOfRangeException(nameof(scanType));

        Id = id;
        TrackingNumber = trackingNumber;
        ScanType = scanType;
        Location = Required(location, LocationMaxLength, nameof(location));
        CourierId = Required(courierId, CourierIdMaxLength, nameof(courierId));
        ScannedAtUtc = scannedAtUtc.ToUniversalTime();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Limit(notes.Trim(), NotesMaxLength, nameof(notes));
        ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
    }

    public TrackingScanId Id { get; }

    public TrackingNumber TrackingNumber { get; }

    public TrackingScanType ScanType { get; }

    public string Location { get; }

    public string CourierId { get; }

    /// <summary>When the courier scanned the parcel, by the device clock.</summary>
    public DateTimeOffset ScannedAtUtc { get; }

    public string? Notes { get; }

    /// <summary>When the server accepted the scan, by the server clock.</summary>
    public DateTimeOffset ReceivedAtUtc { get; }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return Limit(value.Trim(), maximumLength, parameterName);
    }

    private static string Limit(string value, int maximumLength, string parameterName) =>
        value.Length <= maximumLength ? value : throw new ArgumentException($"Must be at most {maximumLength} characters.", parameterName);
}
