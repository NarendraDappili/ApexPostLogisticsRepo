using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Screens;

/// <summary>Pending Scans: what is still waiting for upload, and the scans the server rejected.</summary>
internal sealed class PendingScansScreen(ConsoleScreen screen, IScanRepository scans)
{
    public void Run()
    {
        var pending = scans.ListPending();
        screen.WriteLine($"Pending scans waiting for upload (oldest first): {pending.Count}");
        screen.PrintScans(pending);

        var failed = scans.ListAll().Where(scan => scan.SyncStatus == SyncStatus.Failed).ToList();
        if (failed.Count == 0) return;
        screen.WriteLine();
        screen.WriteLine($"Rejected by the server, not sent again: {failed.Count} (reasons on the Sync Status screen)");
        screen.PrintScans(failed);
    }
}
