using ApexPost.Courier.Sync;
using ApexPost.Courier.Api;
using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.IntegrationTests;

/// <summary>
/// The courier app driven through its screens with scripted input: Login, New Scan, Pending Scans, Sync Now and
/// Sync Status, on a real Couchbase Lite database. Uploads still go to the Part A stand-in server.
/// </summary>
public sealed class CourierAppTests : IDisposable
{
    private readonly CourierAppRun _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task FullSession_SignInScanSyncStatus_SyncsTheGoodScanAndFailsTheFutureOne()
    {
        var inTheFuture = FixedTimes.FarFuture.ToString(ConsoleScreen.TimeFormat, CultureInfo.InvariantCulture);
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(
            [
                "cr-001", "",                                                // login, then Enter: a development token
                "1", "apx-30000001", "1", "Riverside Depot", "Front desk", "", // a pickup, now
                "1", "APX-30000002", "3", "14 Elm Street", "", inTheFuture,  // a delivery dated in the future
                "2",                                                         // pending scans
                "3",                                                         // sync now
                "2",                                                         // pending again
                "4",                                                         // sync status
                "7",
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
        // The API refused the delivery dated in the future with a 400 naming scans[1].scannedAtUtc; only that scan failed.
        Assert.Contains("scannedAtUtc: scannedAtUtc cannot be in the future", output, StringComparison.Ordinal);
        Assert.Equal(0, _app.ExitCode);
        Assert.All(scans, scan => Assert.Equal("CR-001", scan.CourierId));
        Assert.Equal(
            [("APX-30000001", SyncStatus.Synced), ("APX-30000002", SyncStatus.Failed)],
            scans.OrderBy(scan => scan.TrackingNumber).Select(scan => (scan.TrackingNumber, scan.SyncStatus)));
    }

    // Week 5 (S7, TDD cycle 2, TDD-LOG.md): the double-scan guard reaches the courier's New Scan screen.
    [Fact]
    public async Task NewScan_SameDeliveryEnteredTwice_SaysAlreadyRecordedAndKeepsOneScan()
    {
        // Arrange: the same time typed twice, so the two scans are certainly within two minutes of each other.
        var scannedAt = FixedTimes.Now.ToString(ConsoleScreen.TimeFormat, CultureInfo.InvariantCulture);
        IReadOnlyList<ShipmentScan> scans = [];

        // Act
        var output = await _app.RunAsync(
            [
                "CR-001", "",
                "1", "APX-30000003", "3", "14 Elm Street", "", scannedAt,   // a delivery
                "1", "apx-30000003", "3", "14 Elm Street", "", scannedAt,   // the same delivery again, a double tap
                "7",
            ],
            services => scans = CourierAppRun.Scans(services));

        // Assert
        var kept = Assert.Single(scans);
        Assert.Equal(1, CountOf("Saved on this device as Pending", output));
        Assert.Contains($"Already recorded on this device: {kept.Id}. Nothing new was saved.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewScan_Saved_LogsTheChangeAndUpdatesTheLiveCountBeforeTheNextMenu()
    {
        var output = await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "7"]);

        var saved = output.IndexOf("Saved on this device as Pending", StringComparison.Ordinal);
        var logged = output.IndexOf("[info ScanChangeMonitor] Scan documents changed: scan::", StringComparison.Ordinal);
        var nextMenu = output.IndexOf("Choose 1-7", saved, StringComparison.Ordinal);
        Assert.True(saved >= 0 && logged > saved && nextMenu > logged, output);
        Assert.Contains("Pending scans (live): 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_InvalidCourierId_ExplainsAndAsksAgain()
    {
        var output = await _app.RunAsync(["", "CR 001", "cr-007", "", "7"]);

        Assert.Equal(2, CountOf("A courier ID has letters, digits and hyphens only", output));
        Assert.Contains("Signed in as CR-007.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_ScansRecorded_ReturnsToLoginAndKeepsTheScansPending()
    {
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(
            [
                "CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "",
                "6",
                "CR-002", "", "1", "APX-30000002", "1", "North Hub", "", "",
                "7",
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
    public async Task NewScan_InvalidInput_ExplainsWhyAndSavesNothing(string tracking, string type, string location, string message)
    {
        IReadOnlyList<ShipmentScan> scans = [];
        string[] answers = type == "9"
            ? ["CR-001", "", "1", tracking, type, "7"]
            : ["CR-001", "", "1", tracking, type, location, "", message.Contains("time", StringComparison.Ordinal) ? "tomorrow" : "", "7"];

        var output = await _app.RunAsync(answers, services => scans = CourierAppRun.Scans(services));

        Assert.Contains(message, output, StringComparison.Ordinal);
        Assert.Empty(scans);
    }

    [Fact]
    public async Task Restart_ScanSavedEarlier_StaysPendingOnTheDevice()
    {
        await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "7"]);

        var output = await _app.RunAsync(["CR-001", "", "2", "7"]);

        Assert.Contains("Pending scans (live): 1", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans waiting for upload (oldest first): 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ServerAvailable_StoresTheCourierTokenAndTurnsUploadsOn()
    {
        string? token = null;

        var output = await _app.RunAsync(["cr-001", "", "7"], services => token = services.GetRequiredService<ITokenStore>().GetAsync().AsTask().Result);

        Assert.Contains("Signed in as CR-001. Scans upload to the server (sign-in valid until", output, StringComparison.Ordinal);
        Assert.Contains("Uploads: on", output, StringComparison.Ordinal);
        Assert.Equal("fake-token-CR-001#1", token);
        Assert.Equal("""{"name":"CR-001","roles":["Courier"],"courierId":"CR-001"}""", Assert.Single(_app.Api.Server.Requests).Body);
    }

    [Fact]
    public async Task Login_ServerDown_SignsInOfflineAndStillSavesScans()
    {
        _app.Api.Down = true;
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "3", "7"], services => scans = CourierAppRun.Scans(services));

        Assert.Contains("Signed in as CR-001, offline: The server had a problem", output, StringComparison.Ordinal);
        Assert.Contains("Uploads: off (signed in offline)", output, StringComparison.Ordinal);
        Assert.Contains("Nothing was sent: you are signed in offline.", output, StringComparison.Ordinal);
        Assert.Equal(SyncStatus.Pending, Assert.Single(scans).SyncStatus);
        // Only the sign-in was tried: Sync Now sends nothing without a token.
        Assert.Single(_app.Api.Server.Requests);
    }

    [Fact]
    public async Task SignOut_NextCourierSignsIn_GetsTheirOwnToken()
    {
        string? token = null;

        await _app.RunAsync(["CR-001", "", "6", "CR-002", "", "7"], services => token = services.GetRequiredService<ITokenStore>().GetAsync().AsTask().Result);

        Assert.Equal("fake-token-CR-002#2", token);
    }

    [Fact]
    public async Task SyncNow_NoNetwork_SendsNothingAndKeepsTheScansPending()
    {
        IReadOnlyList<ShipmentScan> scans = [];
        _app.Network.IsAvailable = false;

        var output = await _app.RunAsync(["CR-001", "", "1", "APX-30000001", "1", "Riverside Depot", "", "", "3", "7"], services => scans = CourierAppRun.Scans(services));

        Assert.Contains("No network: nothing was sent.", output, StringComparison.Ordinal);
        Assert.Equal(SyncStatus.Pending, Assert.Single(scans).SyncStatus);
        Assert.DoesNotContain(_app.Api.Server.Requests, request => request.Uri.AbsolutePath.EndsWith("/scans", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateHost_DefaultConfiguration_UsesTheSystemNetworkStatus()
    {
        using var host = CourierAppHost.Create([$"--Courier:DataDirectory={_app.DataDirectory}"], TextReader.Null, TextWriter.Null);

        var network = host.Services.GetRequiredService<INetworkStatus>();

        Assert.IsType<SystemNetworkStatus>(network);
        Assert.Equal(SystemNetworkStatus.HasWayOut(), network.IsAvailable);
    }

    [Fact]
    public async Task Login_PipedInputStartsWithByteOrderMark_IgnoresTheMark()
    {
        var output = await _app.RunAsync(["﻿CR-001", "", "7"]);

        Assert.Contains("Signed in as CR-001.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("A courier ID has letters", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InputEnds_SaysGoodbyeAndExitsWithZero()
    {
        var output = await _app.RunAsync([]);

        Assert.Contains("Goodbye", output, StringComparison.Ordinal);
        Assert.Equal(0, _app.ExitCode);
    }

    [Fact]
    public async Task RunAsync_LogLevelWarning_HidesTheInformationLines()
    {
        var output = new StringWriter();
        using (var host = _app.CreateHost(new StringReader("CR-001\n\n1\nAPX-30000001\n1\nDepot\n\n\n7"), output, ["--Logging:LogLevel:Default=Warning"]))
        {
            await host.Services.GetRequiredService<CourierApp>().RunAsync();
        }

        Assert.Contains("Saved on this device as Pending", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[info ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDataDirectory_ConfiguredOrBlank_UsesTheSettingOrLocalAppData()
    {
        using var host = _app.CreateHost(TextReader.Null, TextWriter.Null);

        var configured = host.Services.GetRequiredService<IOptions<CourierAppOptions>>().Value;

        Assert.Equal(Path.GetFullPath(_app.DataDirectory), configured.ResolveDataDirectory());
        Assert.Equal(CourierAppOptions.DefaultDataDirectory, new CourierAppOptions { DataDirectory = " " }.ResolveDataDirectory());
        Assert.EndsWith(Path.Combine("ApexPost", "Courier"), CourierAppOptions.DefaultDataDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateApp_SecondAppOnTheSameFolder_ThrowsDatabaseInUse()
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
