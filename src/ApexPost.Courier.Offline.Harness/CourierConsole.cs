using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Offline.Harness;

/// <summary>
/// The harness menu. It reads and writes through the given reader and writer, so tests can drive a whole session
/// with scripted input. Everything works without a network: the server is the fake uploader.
/// </summary>
internal sealed class CourierConsole
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm";
    private static readonly TimeSpan ListenerSettle = TimeSpan.FromMilliseconds(150);

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly IScanRepository _scans;
    private readonly ScanChangeMonitor _monitor;
    private readonly OutboxService _outbox;
    private readonly FakeShipmentUploader _server;
    private readonly string _courierId;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _listenerWait;
    private readonly SemaphoreSlim _notified = new(0);
    private IReadOnlyList<ScanId> _lastListed = [];

    public CourierConsole(
        TextReader input,
        TextWriter output,
        IScanRepository scans,
        ScanChangeMonitor monitor,
        OutboxService outbox,
        FakeShipmentUploader server,
        string courierId,
        TimeProvider clock,
        TimeSpan? listenerWait = null)
    {
        _input = input;
        _output = output;
        _scans = scans;
        _monitor = monitor;
        _outbox = outbox;
        _server = server;
        _courierId = courierId;
        _clock = clock;
        _listenerWait = listenerWait ?? TimeSpan.FromSeconds(2);
        _monitor.Changed += (_, _) => _notified.Release();
    }

    public async Task RunAsync()
    {
        await OfferSampleDataAsync();
        while (true)
        {
            PrintHeader();
            _output.WriteLine("  1  Add Scan");
            _output.WriteLine("  2  Find by Tracking Number");
            _output.WriteLine("  3  List Pending");
            _output.WriteLine("  4  Simulate Sync");
            _output.WriteLine("  5  Delete Scan");
            _output.WriteLine("  6  Exit");
            var choice = Ask("Choose 1-6");
            if (choice is null or "6")
            {
                _output.WriteLine("Closing the courier database. Goodbye.");
                return;
            }

            _output.WriteLine();
            switch (choice)
            {
                case "1": await AddScanAsync(); break;
                case "2": FindByTrackingNumber(); break;
                case "3": ListPending(); break;
                case "4": await SimulateSyncAsync(); break;
                case "5": await DeleteScanAsync(); break;
                default: _output.WriteLine("Please choose a number from 1 to 6."); break;
            }
        }
    }

    private void PrintHeader()
    {
        var counts = _scans.CountByScanType(_courierId);
        _output.WriteLine();
        _output.WriteLine(new string('-', 78));
        _output.WriteLine($" ApexPost Courier - offline store            courier {_courierId}");
        _output.WriteLine($" Pending scans (live): {_monitor.PendingCount,-4}  Simulated server: {(_server.IsOffline ? "OFFLINE" : "online")}");
        _output.WriteLine($" {_courierId} scans by type: {string.Join("  ", counts.Select(pair => $"{pair.Key} {pair.Value}"))}");
        _output.WriteLine(new string('-', 78));
    }

    private async Task OfferSampleDataAsync()
    {
        if (_scans.ListAll().Count > 0) return;
        if (!Confirm("This device has no scans yet. Add five fictional sample scans?", defaultYes: false)) return;

        Drain();
        foreach (var scan in SampleScans.Create(_courierId, _clock.GetUtcNow())) _scans.Create(scan);
        _output.WriteLine("Sample scans saved locally as Pending.");
        await WaitForListenerAsync();
    }

    private async Task AddScanAsync()
    {
        var trackingNumber = Ask("Tracking number (APX-########)");
        var typeText = Ask("Scan type: 1 Pickup, 2 InTransit, 3 Delivered, 4 Failed");
        if (!int.TryParse(typeText, out var typeNumber) || typeNumber is < 1 or > 4)
        {
            _output.WriteLine("Not saved: choose a scan type from 1 to 4.");
            return;
        }

        var location = Ask("Location");
        var notes = Ask("Notes (optional)");
        var timeText = Ask($"Scanned at, UTC {TimeFormat} (blank = now)");
        DateTimeOffset scannedAt;
        if (string.IsNullOrWhiteSpace(timeText))
        {
            scannedAt = _clock.GetUtcNow();
        }
        else if (!DateTimeOffset.TryParseExact(timeText.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out scannedAt))
        {
            _output.WriteLine($"Not saved: the time must look like {TimeFormat}.");
            return;
        }

        try
        {
            var scan = ShipmentScan.Record(trackingNumber ?? string.Empty, (ScanType)(typeNumber - 1), _courierId, location ?? string.Empty, scannedAt, notes);
            Drain();
            _scans.Create(scan);
            _output.WriteLine($"Saved locally as Pending: {scan.Id}");
            await WaitForListenerAsync();
        }
        catch (ArgumentException exception)
        {
            _output.WriteLine($"Not saved: {FirstSentence(exception)}");
        }
    }

    private void FindByTrackingNumber()
    {
        var trackingNumber = Ask("Tracking number");
        try
        {
            var history = _scans.FindByTrackingNumber(trackingNumber ?? string.Empty);
            _output.WriteLine($"Scan history for {ShipmentScan.NormalizeTrackingNumber(trackingNumber!)} (oldest first): {history.Count} scan(s)");
            PrintTable(history);
        }
        catch (ArgumentException exception)
        {
            _output.WriteLine(FirstSentence(exception));
        }
    }

    private void ListPending()
    {
        var pending = _scans.ListPending();
        _output.WriteLine($"Pending scans waiting for upload (oldest first): {pending.Count}");
        PrintTable(pending);
    }

    private async Task SimulateSyncAsync()
    {
        _server.IsOffline = !Confirm("Is the network available?", defaultYes: true);
        Drain();
        var result = await _outbox.SyncPendingAsync();
        if (result.Synced + result.Failed > 0) await WaitForListenerAsync();
        _output.WriteLine(result.UploadError is null
            ? $"Sync finished: {result.Attempted} sent, {result.Synced} synced, {result.Failed} failed, {result.LeftPending} still pending."
            : $"Sync stopped: {result.UploadError} {result.LeftPending} scan(s) stay Pending and will be retried.");
        foreach (var rejection in result.Rejections) _output.WriteLine($"  Rejected {rejection.ScanId}: {rejection.Reason}");
    }

    private async Task DeleteScanAsync()
    {
        var reference = Ask("Scan to delete (row number from the last list, or the scan ID)")?.Trim();
        ScanId? id = null;
        if (int.TryParse(reference, out var row) && row >= 1 && row <= _lastListed.Count) id = _lastListed[row - 1];
        else if (ScanId.TryParse(reference, out var parsed)) id = parsed;

        if (id is null)
        {
            _output.WriteLine("No such row or scan ID. List the scans first, then give a row number.");
            return;
        }

        Drain();
        if (!_scans.Delete(id))
        {
            _output.WriteLine($"Scan {id} was not found on this device.");
            return;
        }

        _output.WriteLine($"Deleted {id}");
        _lastListed = [.. _lastListed.Where(listed => listed != id)];
        await WaitForListenerAsync();
    }

    private void PrintTable(IReadOnlyList<ShipmentScan> scans)
    {
        _lastListed = [.. scans.Select(scan => scan.Id)];
        if (scans.Count == 0) return;

        _output.WriteLine($"  {"#",-3} {"Scanned (UTC)",-16}  {"Tracking",-12}  {"Type",-9}  {"Location",-24}  {"Sync",-7}  Scan ID");
        for (var i = 0; i < scans.Count; i++)
        {
            var scan = scans[i];
            _output.WriteLine(
                $"  {i + 1,-3} {scan.ScannedAtUtc.ToString(TimeFormat, CultureInfo.InvariantCulture),-16}  {scan.TrackingNumber,-12}  " +
                $"{scan.ScanType,-9}  {Fit(scan.Location, 24),-24}  {scan.SyncStatus,-7}  {scan.Id}");
        }
    }

    /// <summary>Forgets notifications from earlier actions, so the next wait is for this action's change only.</summary>
    private void Drain()
    {
        while (_notified.Wait(0))
        {
        }
    }

    /// <summary>
    /// Change notifications arrive on Couchbase Lite's own thread just after a save. Waiting briefly for them keeps
    /// the listener's log line under the action that caused it, instead of in the middle of the next menu.
    /// </summary>
    private async Task WaitForListenerAsync()
    {
        if (!await _notified.WaitAsync(_listenerWait)) return;
        while (await _notified.WaitAsync(ListenerSettle))
        {
        }
    }

    private string? Ask(string prompt)
    {
        _output.Write($"{prompt}: ");
        var line = _input.ReadLine();
        if (line is null) _output.WriteLine();
        return line;
    }

    private bool Confirm(string question, bool defaultYes)
    {
        var answer = Ask($"{question} {(defaultYes ? "[Y/n]" : "[y/N]")}")?.Trim();
        return string.IsNullOrEmpty(answer) ? defaultYes : answer.StartsWith('y') || answer.StartsWith('Y');
    }

    private static string Fit(string text, int width) => text.Length <= width ? text : string.Concat(text.AsSpan(0, width - 3), "...");

    private static string FirstSentence(ArgumentException exception) => exception.Message.Split(" (Parameter", 2)[0];
}
