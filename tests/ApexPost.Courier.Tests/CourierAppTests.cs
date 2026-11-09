using ApexPost.Courier.Api;
using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Tests;

/// <summary>
/// The courier app driven through its screens with scripted input: Login, New Scan, Pending Scans, Sync Now and
/// Sync Status, on a real Couchbase Lite database. Uploads still go to the Part A stand-in server.
/// </summary>
public sealed class CourierAppTests : IDisposable
{
    private readonly CourierAppRun _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task A_session_signs_in_records_scans_syncs_them_and_shows_the_status()
    {
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToString(ConsoleScreen.TimeFormat, CultureInfo.InvariantCulture);
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(
            [
                "cr-001", "",                                                // login, then Enter: a development token
                "1", "apx-30000001", "1", "Riverside Depot", "Front desk", "", // a pickup, now
                "1", "APX-30000002", "3", "14 Elm Street", "", tomorrow,     // a delivery dated tomorrow
                "2",                                                         // pending scans
                "3",                                                         // sync now
                "2",                                                         // pending again
                "4",                                                         // sync status
                "6",
            ],
            services => scans = CourierAppRun.Scans(services));

        Assert.Contains("Signed in as CR-001.", output, StringComparison.Ordinal);
        Assert.Contains("Saved on this device as Pending: scan::", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans waiting for upload (oldest first): 2", output, StringComparison.Ordinal);
        Assert.Contains("Sync finished: 2 sent, 1 synced, 1 failed, 0 still pending.", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans waiting for upload (oldest first): 0", output, StringComparison.Ordinal);
        Assert.Contains("Rejected by the server, not sent again: 1", output, StringComparison.Ordinal);
        Assert.Contains("1 synced, 1 failed, 0 still pending", output, StringComparison.Ordinal);
        Assert.Contains("Recent failures (newest first): 1", output, StringComparison.Ordinal);
        // The API refused the delivery dated tomorrow with a 400 naming scans[1].scannedAtUtc; only that scan failed.
        Assert.Contains("scannedAtUtc: scannedAtUtc cannot be in the future", output, StringComparison.Ordinal);
        Assert.Equal(0, _app.ExitCode);
        Assert.All(scans, scan => Assert.Equal("CR-001", scan.CourierId));
        Assert.Equal(
            [("APX-30000001", SyncStatus.Synced), ("APX-30000002", SyncStatus.Failed)],
            scans.OrderBy(scan => scan.TrackingNumber).Select(scan => (scan.TrackingNumber, scan.SyncStatus)));
    }

    [Fact]
    public async Task The_listener_log_line_and_the_live_count_follow_the_save()
    {
        var output = await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "6"]);

        var saved = output.IndexOf("Saved on this device as Pending", StringComparison.Ordinal);
        var logged = output.IndexOf("[info ScanChangeMonitor] Scan documents changed: scan::", StringComparison.Ordinal);
        var nextMenu = output.IndexOf("Choose 1-6", saved, StringComparison.Ordinal);
        Assert.True(saved >= 0 && logged > saved && nextMenu > logged, output);
        Assert.Contains("Pending scans (live): 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_explains_a_bad_courier_id_and_asks_again()
    {
        var output = await _app.RunAsync(["", "CR 001", "cr-007", "", "6"]);

        Assert.Equal(2, CountOf("A courier ID has letters, digits and hyphens only", output));
        Assert.Contains("Signed in as CR-007.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signing_out_returns_to_login_and_keeps_the_scans_on_the_device()
    {
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(
            [
                "CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "",
                "5",
                "CR-002", "", "1", "APX-30000002", "1", "North Hub", "", "",
                "6",
            ],
            services => scans = CourierAppRun.Scans(services));

        Assert.Contains("Signed out CR-001.", output, StringComparison.Ordinal);
        Assert.Contains("signed in as CR-002", output, StringComparison.Ordinal);
        Assert.Equal(["CR-001", "CR-002"], scans.Select(scan => scan.CourierId).Order());
        Assert.All(scans, scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
    }

    [Theory]
    [InlineData("APX-1", "1", "Depot", "Not saved: A tracking number must be APX- followed by eight digits.")]
    [InlineData("APX-30000001", "9", "Depot", "Not saved: choose a scan type from 1 to 4.")]
    [InlineData("APX-30000001", "1", "", "Not saved: The value cannot be an empty string or composed entirely of whitespace.")]
    [InlineData("APX-30000001", "1", "Depot", "Not saved: the time must look like yyyy-MM-dd HH:mm.")]
    public async Task Bad_scan_input_is_explained_and_nothing_is_saved(string tracking, string type, string location, string message)
    {
        IReadOnlyList<ShipmentScan> scans = [];
        string[] answers = type == "9"
            ? ["CR-001", "", "1", tracking, type, "6"]
            : ["CR-001", "", "1", tracking, type, location, "", message.Contains("time", StringComparison.Ordinal) ? "tomorrow" : "", "6"];

        var output = await _app.RunAsync(answers, services => scans = CourierAppRun.Scans(services));

        Assert.Contains(message, output, StringComparison.Ordinal);
        Assert.Empty(scans);
    }

    [Fact]
    public async Task Scans_stay_pending_on_the_device_across_restarts()
    {
        await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "6"]);

        var output = await _app.RunAsync(["CR-001", "", "2", "6"]);

        Assert.Contains("Pending scans (live): 1", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans waiting for upload (oldest first): 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_gets_a_token_for_the_courier_and_uploads_are_on()
    {
        string? token = null;

        var output = await _app.RunAsync(["cr-001", "", "6"], services => token = services.GetRequiredService<ITokenStore>().GetAsync().AsTask().Result);

        Assert.Contains("Signed in as CR-001. Scans upload to the server (sign-in valid until", output, StringComparison.Ordinal);
        Assert.Contains("Uploads: on", output, StringComparison.Ordinal);
        Assert.Equal("fake-token-CR-001#1", token);
        Assert.Equal("""{"name":"CR-001","roles":["Courier"],"courierId":"CR-001"}""", Assert.Single(_app.Api.Server.Requests).Body);
    }

    [Fact]
    public async Task Without_the_server_the_courier_signs_in_offline_and_scans_are_still_saved()
    {
        _app.Api.Down = true;
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "3", "6"], services => scans = CourierAppRun.Scans(services));

        Assert.Contains("Signed in as CR-001, offline: The server had a problem", output, StringComparison.Ordinal);
        Assert.Contains("Uploads: off (signed in offline)", output, StringComparison.Ordinal);
        Assert.Contains("Nothing was sent: you are signed in offline.", output, StringComparison.Ordinal);
        Assert.Equal(SyncStatus.Pending, Assert.Single(scans).SyncStatus);
        // Only the sign-in was tried: Sync Now sends nothing without a token.
        Assert.Single(_app.Api.Server.Requests);
    }

    [Fact]
    public async Task Signing_out_forgets_the_token_and_the_next_courier_gets_their_own()
    {
        string? token = null;

        await _app.RunAsync(["CR-001", "", "5", "CR-002", "", "6"], services => token = services.GetRequiredService<ITokenStore>().GetAsync().AsTask().Result);

        Assert.Equal("fake-token-CR-002#2", token);
    }

    [Fact]
    public async Task A_byte_order_mark_at_the_start_of_piped_input_is_ignored()
    {
        var output = await _app.RunAsync(["﻿CR-001", "", "6"]);

        Assert.Contains("Signed in as CR-001.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("A courier ID has letters", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task End_of_input_closes_the_app()
    {
        var output = await _app.RunAsync([]);

        Assert.Contains("Goodbye", output, StringComparison.Ordinal);
        Assert.Equal(0, _app.ExitCode);
    }

    [Fact]
    public async Task The_logging_configuration_decides_which_log_lines_are_shown()
    {
        var output = new StringWriter();
        using (var host = _app.CreateHost(new StringReader("CR-001\n\n1\nAPX-30000001\n1\nDepot\n\n\n6"), output, ["--Logging:LogLevel:Default=Warning"]))
        {
            await host.Services.GetRequiredService<CourierApp>().RunAsync();
        }

        Assert.Contains("Saved on this device as Pending", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[info ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_database_folder_comes_from_configuration_or_defaults_to_local_app_data()
    {
        using var host = _app.CreateHost(TextReader.Null, TextWriter.Null);

        var configured = host.Services.GetRequiredService<IOptions<CourierAppOptions>>().Value;

        Assert.Equal(Path.GetFullPath(_app.DataDirectory), configured.ResolveDataDirectory());
        Assert.Equal(CourierAppOptions.DefaultDataDirectory, new CourierAppOptions { DataDirectory = " " }.ResolveDataDirectory());
        Assert.EndsWith(Path.Combine("ApexPost", "Courier"), CourierAppOptions.DefaultDataDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_app_on_the_same_folder_is_refused()
    {
        using var first = _app.CreateHost(TextReader.Null, TextWriter.Null);
        _ = first.Services.GetRequiredService<CourierApp>();
        using var second = _app.CreateHost(TextReader.Null, TextWriter.Null);

        Assert.Throws<CourierDatabaseInUseException>(() => second.Services.GetRequiredService<CourierApp>());
    }

    private static int CountOf(string text, string output)
    {
        var count = 0;
        for (var at = output.IndexOf(text, StringComparison.Ordinal); at >= 0; at = output.IndexOf(text, at + text.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
