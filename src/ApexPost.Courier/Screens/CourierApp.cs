using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Sync;

namespace ApexPost.Courier.Screens;

/// <summary>
/// The app's main loop: Login first, then the menu with New Scan, Pending Scans, Sync Now and Sync Status. Every
/// scan is saved on the device before anything is sent, so the app works the same with or without a network.
/// </summary>
internal sealed class CourierApp(
    ConsoleScreen screen,
    CourierSession session,
    ScanChangeMonitor monitor,
    SyncService sync,
    SyncHistory history,
    LoginScreen login,
    NewScanScreen newScan,
    PendingScansScreen pendingScans,
    SyncStatusScreen syncStatus)
{
    public async Task<int> RunAsync()
    {
        screen.WriteLine("ApexPost Courier - scans are saved on this device first and uploaded when you sync.");
        while (true)
        {
            if (!session.IsSignedIn && !login.Run()) return Goodbye();

            PrintHeader();
            screen.WriteLine("  1  New Scan");
            screen.WriteLine("  2  Pending Scans");
            screen.WriteLine("  3  Sync Now");
            screen.WriteLine("  4  Sync Status");
            screen.WriteLine("  5  Sign Out");
            screen.WriteLine("  6  Exit");
            var choice = screen.Ask("Choose 1-6")?.Trim();
            if (choice is null or "6") return Goodbye();

            screen.WriteLine();
            switch (choice)
            {
                case "1": await newScan.RunAsync(); break;
                case "2": pendingScans.Run(); break;
                case "3": await SyncNowAsync(); break;
                case "4": syncStatus.Run(); break;
                case "5": SignOut(); break;
                default: screen.WriteLine("Please choose a number from 1 to 6."); break;
            }
        }
    }

    private void PrintHeader()
    {
        var last = history.LastRun;
        screen.WriteLine();
        screen.WriteLine(new string('-', 78));
        screen.WriteLine($" ApexPost Courier                                   signed in as {session.CourierId}");
        screen.WriteLine($" Pending scans (live): {monitor.PendingCount,-4}  Last sync: {(last is null ? "never" : $"{ConsoleScreen.Time(last.FinishedAtUtc)} UTC")}");
        screen.WriteLine(new string('-', 78));
    }

    private async Task SyncNowAsync()
    {
        screen.ExpectChange();
        var result = await sync.SyncNowAsync();
        if (result.AlreadyRunning)
        {
            screen.WriteLine("A sync is already running.");
            return;
        }

        if (result.Synced + result.Failed > 0) await screen.WaitForChangeAsync();
        screen.WriteLine(result.UploadError is null
            ? $"Sync finished: {result.Attempted} sent, {result.Synced} synced, {result.Failed} failed, {result.LeftPending} still pending."
            : $"Sync stopped: {result.UploadError} {result.LeftPending} scan(s) stay Pending and will be sent on the next sync.");
        foreach (var rejection in result.Rejections) screen.WriteLine($"  Rejected {rejection.ScanId}: {rejection.Reason}");
    }

    private void SignOut()
    {
        screen.WriteLine($"Signed out {session.CourierId}. Scans on this device stay and are uploaded on the next sync.");
        session.SignOut();
    }

    private int Goodbye()
    {
        screen.WriteLine("Closing the courier database. Goodbye.");
        return 0;
    }
}
