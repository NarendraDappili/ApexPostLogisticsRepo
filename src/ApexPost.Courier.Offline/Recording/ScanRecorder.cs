using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Recording;

/// <summary>
/// Records a scan on the device, as the New Scan screen does today: the scan is saved as Pending, dated now unless
/// the courier gave a time. It lives here, not in a screen, so every app head (console, MAUI) shares it and it can be
/// tested without a user interface. The time comes from the injected clock.
/// </summary>
public sealed class ScanRecorder(IScanRepository scans, TimeProvider clock)
{
    public ScanRecordResult Record(string trackingNumber, ScanType scanType, string courierId, string location, DateTimeOffset? scannedAtUtc = null, string? notes = null)
    {
        var scan = ShipmentScan.Record(trackingNumber, scanType, courierId, location, scannedAtUtc ?? clock.GetUtcNow(), notes);
        scans.Create(scan);
        return new ScanRecordResult(scan, AlreadyRecorded: false);
    }
}

/// <summary>The scan the courier now has on the device, and whether it was recorded earlier rather than just now.</summary>
public sealed record ScanRecordResult(ShipmentScan Scan, bool AlreadyRecorded);
