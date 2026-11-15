using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Recording;

namespace ApexPost.Courier.Screens;

/// <summary>New Scan: records a scan for the signed-in courier and saves it on the device as Pending.</summary>
internal sealed class NewScanScreen(ConsoleScreen screen, CourierSession session, ScanRecorder recorder, TimeProvider clock)
{
    private static readonly ScanType[] ScanTypes = Enum.GetValues<ScanType>();

    public async Task RunAsync()
    {
        var trackingNumber = screen.Ask("Tracking number (APX-########)");
        var typeText = screen.Ask($"Scan type: {string.Join(", ", ScanTypes.Select((type, i) => $"{i + 1} {type}"))}");
        if (!int.TryParse(typeText, out var typeNumber) || typeNumber < 1 || typeNumber > ScanTypes.Length)
        {
            screen.WriteLine($"Not saved: choose a scan type from 1 to {ScanTypes.Length}.");
            return;
        }

        var location = screen.Ask("Location");
        var notes = screen.Ask("Notes (optional)");
        var timeText = screen.Ask($"Scanned at, UTC {ConsoleScreen.TimeFormat} (blank = now)");
        DateTimeOffset scannedAt;
        if (string.IsNullOrWhiteSpace(timeText))
        {
            scannedAt = clock.GetUtcNow();
        }
        else if (!DateTimeOffset.TryParseExact(timeText.Trim(), ConsoleScreen.TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out scannedAt))
        {
            screen.WriteLine($"Not saved: the time must look like {ConsoleScreen.TimeFormat}.");
            return;
        }

        try
        {
            screen.ExpectChange();
            var result = recorder.Record(trackingNumber ?? string.Empty, ScanTypes[typeNumber - 1], session.CourierId!, location ?? string.Empty, scannedAt, notes);
            if (result.AlreadyRecorded)
            {
                screen.WriteLine($"Already recorded on this device: {result.Scan.Id}. Nothing new was saved.");
                return;
            }

            var scan = result.Scan;
            screen.WriteLine($"Saved on this device as Pending: {scan.Id}");
            await screen.WaitForChangeAsync();
        }
        catch (ArgumentException exception)
        {
            screen.WriteLine($"Not saved: {ConsoleScreen.FirstSentence(exception)}");
        }
    }
}
