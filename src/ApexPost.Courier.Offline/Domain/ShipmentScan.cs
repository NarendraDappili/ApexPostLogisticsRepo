using System.Text.RegularExpressions;

namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// One courier scan of a parcel, recorded on the device. A new scan is always <see cref="SyncStatus.Pending"/>: it is
/// saved locally first and the outbox uploads it later. This class knows nothing about how it is stored.
/// </summary>
public sealed partial class ShipmentScan
{
    public const int MaximumCourierIdLength = 32;
    public const int MaximumLocationLength = 100;
    public const int MaximumNotesLength = 500;

    /// <summary>Rebuilds a scan exactly as stored; every value is validated again.</summary>
    public ShipmentScan(
        ScanId id,
        string trackingNumber,
        ScanType scanType,
        string courierId,
        string location,
        DateTimeOffset scannedAtUtc,
        string? notes,
        SyncStatus syncStatus)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(scanType)) throw new ArgumentOutOfRangeException(nameof(scanType));
        if (!Enum.IsDefined(syncStatus)) throw new ArgumentOutOfRangeException(nameof(syncStatus));
        if (scannedAtUtc == default) throw new ArgumentOutOfRangeException(nameof(scannedAtUtc), "The scan time is required.");

        Id = id;
        TrackingNumber = NormalizeTrackingNumber(trackingNumber);
        ScanType = scanType;
        CourierId = Required(courierId, MaximumCourierIdLength, nameof(courierId));
        Location = Required(location, MaximumLocationLength, nameof(location));
        ScannedAtUtc = scannedAtUtc.ToUniversalTime();
        Notes = Optional(notes, MaximumNotesLength, nameof(notes));
        SyncStatus = syncStatus;
    }

    public ScanId Id { get; }

    /// <summary>The parcel's tracking number, <c>APX-</c> followed by eight digits.</summary>
    public string TrackingNumber { get; }

    public ScanType ScanType { get; }

    public string CourierId { get; }

    public string Location { get; private set; }

    public DateTimeOffset ScannedAtUtc { get; }

    public string? Notes { get; private set; }

    public SyncStatus SyncStatus { get; private set; }

    /// <summary>Records a new scan with a fresh <c>scan::{Guid}</c> ID, waiting to be uploaded.</summary>
    public static ShipmentScan Record(
        string trackingNumber,
        ScanType scanType,
        string courierId,
        string location,
        DateTimeOffset scannedAtUtc,
        string? notes = null) =>
        new(ScanId.New(), trackingNumber, scanType, courierId, location, scannedAtUtc, notes, SyncStatus.Pending);

    /// <summary>
    /// Corrects what the courier typed. A synced scan is already on the server, which skips re-uploads of the same
    /// ID, so a correction there would never arrive: it has to be a new scan instead.
    /// </summary>
    public void Correct(string location, string? notes)
    {
        if (SyncStatus == SyncStatus.Synced) throw new InvalidOperationException("A synced scan cannot be changed; record a new scan instead.");

        Location = Required(location, MaximumLocationLength, nameof(location));
        Notes = Optional(notes, MaximumNotesLength, nameof(notes));
        SyncStatus = SyncStatus.Pending;
    }

    public void MarkSynced() => SyncStatus = SyncStatus.Synced;

    public void MarkFailed() => SyncStatus = SyncStatus.Failed;

    /// <summary>Trims and upper-cases a tracking number and checks it is <c>APX-</c> plus eight digits.</summary>
    public static string NormalizeTrackingNumber(string trackingNumber)
    {
        ArgumentNullException.ThrowIfNull(trackingNumber);
        var normalized = trackingNumber.Trim().ToUpperInvariant();
        if (!TrackingNumberFormat().IsMatch(normalized))
        {
            throw new ArgumentException("A tracking number must be APX- followed by eight digits.", nameof(trackingNumber));
        }

        return normalized;
    }

    [GeneratedRegex(@"^APX-\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex TrackingNumberFormat();

    private static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, maximumLength, parameterName);
        return trimmed;
    }

    private static string? Optional(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, parameterName);
}
