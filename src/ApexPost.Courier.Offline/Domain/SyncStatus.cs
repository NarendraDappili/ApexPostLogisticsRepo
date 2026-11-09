namespace ApexPost.Courier.Offline.Domain;

/// <summary>Whether a locally saved scan has reached the ApexPost server yet.</summary>
public enum SyncStatus
{
    /// <summary>Saved on the device only; the outbox still has to upload it.</summary>
    Pending,

    /// <summary>The server accepted the scan (or already had it).</summary>
    Synced,

    /// <summary>The server rejected the scan; it needs the courier's attention.</summary>
    Failed,
}
