using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Recording;

namespace ApexPost.Courier.Screens;

/// <summary>
/// New Scan: asks the signed-in courier for a scan and records it through <see cref="ScanRecorder"/>, which saves it on
/// the device as Pending, dates it now when no time is typed, and keeps the first of a double scan (TDD-LOG.md). The
/// screen only asks and answers; the rules live in the recorder, which the MAUI app will share.
/// </summary>
internal sealed class NewScanScreen(ConsoleScreen screen, CourierSession session, ScanRecorder recorder)
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
        DateTimeOffset? scannedAt = null;
        if (!string.IsNullOrWhiteSpace(timeText))
        {
            if (!DateTimeOffset.TryParseExact(timeText.Trim(), ConsoleScreen.TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var typed))
            {
                screen.WriteLine($"Not saved: the time must look like {ConsoleScreen.TimeFormat}.");
                return;
            }

            scannedAt = typed;
        }

        try
        {
            screen.ExpectChange();
            var (scan, alreadyRecorded) = recorder.Record(trackingNumber ?? string.Empty, ScanTypes[typeNumber - 1], session.CourierId!, location ?? string.Empty, scannedAt, notes);
            if (alreadyRecorded)
            {
                // Nothing was written, so there is no change notification to wait for.
                screen.WriteLine($"Already recorded on this device: {scan.Id}. Nothing new was saved.");
                return;
            }

            screen.WriteLine($"Saved on this device as Pending: {scan.Id}");
            await screen.WaitForChangeAsync();
        }
        catch (ArgumentException exception)
        {
            screen.WriteLine($"Not saved: {ConsoleScreen.FirstSentence(exception)}");
        }
    }
}
