using ApexPost.Courier.Api;
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
    ServerAvailability server,
    ITokenStore tokens,
    INetworkStatus network,
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
            // The server refused the token (401) or it expired, perhaps during a background sync: back to Login. The
            // token is already gone from the store; the scans stay on the device.
            if (session.SignInEndedReason is { } reason)
            {
                screen.WriteLine();
                screen.WriteLine(reason);
                session.SignOut();
            }

            if (!session.IsSignedIn && !await login.RunAsync()) return Goodbye();

            await PrintHeaderAsync();
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
                case "5": await SignOutAsync(); break;
                default: screen.WriteLine("Please choose a number from 1 to 6."); break;
            }
        }
    }

    private async Task PrintHeaderAsync()
    {
        var last = history.LastRun;
        var uploads = await tokens.GetAsync() is null ? "off (signed in offline)"
            : tokens.ExpiresAtUtc is { } expiresAt ? $"on until {expiresAt.UtcDateTime:HH:mm} UTC" : "on";
        screen.WriteLine();
        screen.WriteLine(new string('-', 78));
        screen.WriteLine($" ApexPost Courier                                   signed in as {session.CourierId}");
        screen.WriteLine($" Pending scans (live): {monitor.PendingCount,-4}  Last sync: {(last is null ? "never" : $"{ConsoleScreen.Time(last.FinishedAtUtc)} UTC"),-20}  Uploads: {uploads}");
        // The circuit breaker is open or testing the server again: scans are still saved, and wait on the device.
        if (server.IsWorkingOffline) screen.WriteLine($" *** {ServerAvailability.Banner} ***");
        screen.WriteLine(new string('-', 78));
    }

    private async Task SyncNowAsync()
    {
        // The sign-in ended while the menu was waiting (a background sync got a 401): the loop says why and goes to Login.
        if (session.SignInEndedReason is not null) return;

        // As a phone app checks for a connection first: without one nothing is sent, even to an API on this machine.
        if (!network.IsAvailable)
        {
            screen.WriteLine("No network: nothing was sent. Your scans are saved on this device and are sent by themselves when the network comes back.");
            return;
        }

        if (await tokens.GetAsync() is null)
        {
            screen.WriteLine("Nothing was sent: you are signed in offline. Your scans are saved on this device; sign out and in again with a network to upload them.");
            return;
        }

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
            : $"Sync stopped: {result.UploadError} ({result.LeftPending} scan(s) Pending.)");
        foreach (var rejection in result.Rejections) screen.WriteLine($"  Rejected {rejection.ScanId}: {rejection.Reason}");
    }

    private async Task SignOutAsync()
    {
        screen.WriteLine($"Signed out {session.CourierId}. Scans on this device stay and are uploaded after the courier signs in again.");
        session.SignOut();
        await tokens.ClearAsync();
    }

    private int Goodbye()
    {
        screen.WriteLine("Closing the courier database. Goodbye.");
        return 0;
    }
}
