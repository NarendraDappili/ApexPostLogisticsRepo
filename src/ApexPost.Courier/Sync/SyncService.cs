using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Sync;

/// <summary>Runs the Part A outbox and records each run for the Sync Status screen.</summary>
internal sealed class SyncService(OutboxService outbox, SyncHistory history, TimeProvider clock)
{
    /// <summary>Uploads the pending scans once. A run already in progress is not recorded twice.</summary>
    public async Task<OutboxSyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var result = await outbox.SyncPendingAsync(cancellationToken);
        if (!result.AlreadyRunning) history.Record(new SyncRun(clock.GetUtcNow(), result));
        return result;
    }
}
