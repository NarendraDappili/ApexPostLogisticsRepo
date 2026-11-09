using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Sync;

namespace ApexPost.Courier.Screens;

/// <summary>
/// Out for Delivery: tells the server the courier has set off with a shipment. The scan types stop at InTransit
/// before Delivered, and the server takes a Delivered or Failed scan only from OutForDelivery, so without this step a
/// delivery scan would always be refused. Unlike a scan it changes the server's status at once, so it needs a
/// connection and a token. Pending scans go first, so the server has seen the Pickup and InTransit that come before.
/// </summary>
internal sealed class OutForDeliveryScreen(
    ConsoleScreen screen,
    CourierSession session,
    ApexApiClient api,
    ITokenStore tokens,
    INetworkStatus network,
    ScanChangeMonitor monitor,
    SyncService sync)
{
    public async Task RunAsync()
    {
        // The sign-in ended while the menu was waiting: the main loop says why and goes to Login.
        if (session.SignInEndedReason is not null) return;

        if (!network.IsAvailable)
        {
            screen.WriteLine("No network: the server was not told. Try again when there is a network, before recording the delivery.");
            return;
        }

        if (await tokens.GetAsync() is null)
        {
            screen.WriteLine("The server was not told: you are signed in offline. Sign out and in again with a network first.");
            return;
        }

        string trackingNumber;
        try
        {
            trackingNumber = ShipmentScan.NormalizeTrackingNumber(screen.Ask("Tracking number (APX-########)") ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            screen.WriteLine($"Not sent: {ConsoleScreen.FirstSentence(exception)}");
            return;
        }

        try
        {
            if (monitor.PendingCount > 0)
            {
                screen.ExpectChange();
                var uploaded = await sync.SyncNowAsync();
                if (uploaded.Synced + uploaded.Failed > 0) await screen.WaitForChangeAsync();
            }

            await api.MarkOutForDeliveryAsync(trackingNumber);
            screen.WriteLine($"{trackingNumber} is out for delivery. Its Delivered or Failed scan will now be accepted.");
        }
        catch (Exception exception) when (exception is ApexApiException or ApiUnavailableException)
        {
            // After a 401 the main loop explains and goes to Login; the same news twice would only confuse.
            if (session.SignInEndedReason is null) screen.WriteLine($"The status was not changed: {exception.Message}");
        }
    }
}
