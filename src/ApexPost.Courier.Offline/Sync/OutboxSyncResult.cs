using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Sync;

/// <summary>What one outbox run did.</summary>
/// <param name="Attempted">Pending scans sent to the uploader.</param>
/// <param name="Synced">Scans the server accepted or already had.</param>
/// <param name="Failed">Scans the server rejected; see <paramref name="Rejections"/>.</param>
/// <param name="LeftPending">Scans still Pending: not sent because the upload failed, not answered, or changed meanwhile.</param>
/// <param name="Rejections">Each rejected scan with the server's reason.</param>
/// <param name="UploadError">Why the upload stopped early (for example no network); <see langword="null"/> if it did not.</param>
/// <param name="AlreadyRunning">Another run was in progress, so this one did nothing.</param>
public sealed record OutboxSyncResult(
    int Attempted,
    int Synced,
    int Failed,
    int LeftPending,
    IReadOnlyList<ScanUploadOutcome> Rejections,
    string? UploadError,
    bool AlreadyRunning = false)
{
    internal static OutboxSyncResult SkippedBecauseRunning { get; } = new(0, 0, 0, 0, [], null, AlreadyRunning: true);
}
