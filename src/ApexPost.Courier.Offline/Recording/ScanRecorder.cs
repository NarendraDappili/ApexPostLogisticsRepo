using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Recording;

/// <summary>
/// Records a courier's scan on the device, as Pending, dated now unless the courier gave a time. It lives here, not in
/// a screen, so every app head (console, MAUI) shares it and it can be tested without a user interface. The time
/// comes from the injected clock.
/// <para>
/// The double-scan guard (TDD-LOG.md): a repeat of a scan already on the device is not saved again. A repeat is the
/// same parcel, type and courier within <see cref="ScanRecorderOptions.RepeatWindow"/>
/// (<see cref="ShipmentScan.IsRepeatOf"/>). The courier gets the earlier scan back, marked as already recorded.
/// </para>
/// </summary>
public sealed class ScanRecorder(IScanRepository scans, TimeProvider clock, ScanRecorderOptions? options = null)
{
    private readonly TimeSpan _repeatWindow = (options ?? new ScanRecorderOptions()).RepeatWindow;

    public ScanRecordResult Record(string trackingNumber, ScanType scanType, string courierId, string location, DateTimeOffset? scannedAtUtc = null, string? notes = null)
    {
        // Built first, so the comparison sees the stored form (normalised tracking number, trimmed courier ID), and an
        // invalid scan is refused before anything is read.
        var scan = ShipmentScan.Record(trackingNumber, scanType, courierId, location, scannedAtUtc ?? clock.GetUtcNow(), notes);

        var earlier = scans.FindByTrackingNumber(scan.TrackingNumber).FirstOrDefault(stored => scan.IsRepeatOf(stored, _repeatWindow));
        if (earlier is not null) return new ScanRecordResult(earlier, AlreadyRecorded: true);

        scans.Create(scan);
        return new ScanRecordResult(scan, AlreadyRecorded: false);
    }
}

public sealed class ScanRecorderOptions
{
    /// <summary>Two minutes: longer than any double tap, shorter than two real visits to the same door.</summary>
    public static readonly TimeSpan DefaultRepeatWindow = TimeSpan.FromMinutes(2);

    /// <summary>How close two identical scans must be to count as one event.</summary>
    public TimeSpan RepeatWindow { get; init; } = DefaultRepeatWindow;
}

/// <summary>The scan the courier now has on the device, and whether it was recorded earlier rather than just now.</summary>
public sealed record ScanRecordResult(ShipmentScan Scan, bool AlreadyRecorded);
