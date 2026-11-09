namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Sends pending scans to the ApexPost server. Part A uses a fake; Part D replaces it with the real API client.
/// The call may fail as a whole (no network), which leaves every scan Pending for the next attempt.
/// </summary>
public interface IShipmentUploader
{
    /// <summary>Uploads the scans and returns one outcome per scan.</summary>
    Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> scans, CancellationToken cancellationToken = default);
}

/// <summary>What the server did with one uploaded scan.</summary>
public enum ScanUploadStatus
{
    /// <summary>Stored for the first time.</summary>
    Accepted,

    /// <summary>The server already had this scan ID, so it is safe to treat it as synced.</summary>
    Duplicate,

    /// <summary>The server refused the scan; <see cref="ScanUploadOutcome.Reason"/> says why.</summary>
    Rejected,
}

public sealed record ScanUploadOutcome(ScanId ScanId, ScanUploadStatus Status, string? Reason = null);
