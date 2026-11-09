using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;

namespace ApexPost.Courier.Screens;

/// <summary>
/// What every screen shares: prompts, the scan table, and waiting for the change listener so its log line appears
/// under the action that caused it instead of in the middle of the next menu.
/// </summary>
internal sealed class ConsoleScreen
{
    public const string TimeFormat = "yyyy-MM-dd HH:mm";
    private static readonly TimeSpan ListenerSettle = TimeSpan.FromMilliseconds(150);

    private readonly TextReader _input;
    private readonly TimeSpan _listenerWait;
    private readonly SemaphoreSlim _notified = new(0);

    public ConsoleScreen(TextReader input, TextWriter output, ScanChangeMonitor monitor, TimeSpan? listenerWait = null)
    {
        _input = input;
        Output = output;
        _listenerWait = listenerWait ?? TimeSpan.FromSeconds(2);
        monitor.Changed += (_, _) => _notified.Release();
    }

    public TextWriter Output { get; }

    public void WriteLine(string text = "") => Output.WriteLine(text);

    /// <summary>Asks for one line; <see langword="null"/> when the input has ended.</summary>
    public string? Ask(string prompt)
    {
        Output.Write($"{prompt}: ");
        var line = _input.ReadLine();
        if (line is null) Output.WriteLine();
        // Input piped from Windows PowerShell starts with a byte order mark, which would make the first answer invalid.
        return line?.TrimStart('﻿');
    }

    public bool Confirm(string question, bool defaultYes)
    {
        var answer = Ask($"{question} {(defaultYes ? "[Y/n]" : "[y/N]")}")?.Trim();
        return string.IsNullOrEmpty(answer) ? defaultYes : answer.StartsWith('y') || answer.StartsWith('Y');
    }

    /// <summary>Forgets notifications from earlier actions; call it just before the change to wait for.</summary>
    public void ExpectChange()
    {
        while (_notified.Wait(0))
        {
        }
    }

    /// <summary>
    /// Waits briefly for the change notifications of the action just taken. Couchbase Lite raises them on its own
    /// thread just after the save.
    /// </summary>
    public async Task WaitForChangeAsync()
    {
        if (!await _notified.WaitAsync(_listenerWait)) return;
        while (await _notified.WaitAsync(ListenerSettle))
        {
        }
    }

    public void PrintScans(IReadOnlyList<ShipmentScan> scans)
    {
        if (scans.Count == 0) return;

        Output.WriteLine($"  {"#",-3} {"Scanned (UTC)",-16}  {"Tracking",-12}  {"Type",-9}  {"Location",-24}  {"Courier",-8}  Scan ID");
        for (var i = 0; i < scans.Count; i++)
        {
            var scan = scans[i];
            Output.WriteLine(
                $"  {i + 1,-3} {Time(scan.ScannedAtUtc),-16}  {scan.TrackingNumber,-12}  " +
                $"{scan.ScanType,-9}  {Fit(scan.Location, 24),-24}  {Fit(scan.CourierId, 8),-8}  {scan.Id}");
        }
    }

    public static string Time(DateTimeOffset value) => value.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static string FirstSentence(ArgumentException exception) => exception.Message.Split(" (Parameter", 2)[0];

    private static string Fit(string text, int width) => text.Length <= width ? text : string.Concat(text.AsSpan(0, width - 3), "...");
}
