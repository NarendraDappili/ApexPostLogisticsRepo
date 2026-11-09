using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Sync;

/// <summary>One finished outbox run and when it ended.</summary>
internal sealed record SyncRun(DateTimeOffset FinishedAtUtc, OutboxSyncResult Result)
{
    /// <summary>The run reached the server: no upload failed as a whole.</summary>
    public bool ReachedServer => Result.UploadError is null;
}

/// <summary>Something that went wrong in a sync: a rejected scan, or an upload that failed as a whole.</summary>
internal sealed record SyncFailure(DateTimeOffset AtUtc, string What, string Reason);

/// <summary>
/// What the Sync Status screen shows: the last run, the last time the server was reached and the latest failures.
/// Kept in memory for this session; the scans themselves keep their Pending, Synced or Failed state on the device.
/// </summary>
internal sealed class SyncHistory
{
    /// <summary>How many failures are kept, newest first.</summary>
    public const int KeptFailures = 20;

    private readonly Lock _gate = new();
    private readonly LinkedList<SyncFailure> _failures = new();

    public SyncRun? LastRun { get; private set; }

    public DateTimeOffset? LastReachedServerAtUtc { get; private set; }

    public IReadOnlyList<SyncFailure> RecentFailures
    {
        get
        {
            lock (_gate) return [.. _failures];
        }
    }

    public void Record(SyncRun run)
    {
        lock (_gate)
        {
            LastRun = run;
            if (run.ReachedServer) LastReachedServerAtUtc = run.FinishedAtUtc;
            else Add(new SyncFailure(run.FinishedAtUtc, "Upload", run.Result.UploadError!));

            foreach (var rejection in run.Result.Rejections) Add(new SyncFailure(run.FinishedAtUtc, rejection.ScanId.ToString(), rejection.Reason ?? "Rejected."));
        }
    }

    private void Add(SyncFailure failure)
    {
        _failures.AddFirst(failure);
        while (_failures.Count > KeptFailures) _failures.RemoveLast();
    }
}
