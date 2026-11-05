using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Harness;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Courier.Offline.Tests.Sync;

namespace ApexPost.Courier.Offline.Tests.Harness;

public sealed class CourierConsoleTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly TemporaryCourierDatabase _temporary = new();
    private readonly CouchbaseScanRepository _repository;

    public CourierConsoleTests()
    {
        _repository = new CouchbaseScanRepository(_temporary.Database);
    }

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task A_full_session_adds_finds_lists_syncs_and_deletes()
    {
        var output = await Run(
            "n",                                                        // no sample data
            "1", "apx-30000001", "1", "Riverside Depot", "Front desk", "", // add a pickup, now
            "1", "APX-30000001", "3", "14 Elm Street", "", "2026-11-05 13:30", // add a delivery 90 minutes ahead
            "2", "APX-30000001",                                          // find by tracking number
            "3",                                                          // list pending
            "4", "n",                                                     // sync with no network
            "4", "y",                                                     // sync again online
            "3",                                                          // nothing pending now
            "2", "APX-30000001",                                          // list again to get row numbers
            "5", "1",                                                     // delete row 1
            "6");

        Assert.Contains("Saved locally as Pending: scan::", output, StringComparison.Ordinal);
        Assert.Contains("Scan history for APX-30000001 (oldest first): 2 scan(s)", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans waiting for upload (oldest first): 2", output, StringComparison.Ordinal);
        Assert.Contains("Sync stopped: The ApexPost server cannot be reached (simulated: no network). 2 scan(s) stay Pending", output, StringComparison.Ordinal);
        Assert.Contains("Sync finished: 2 sent, 1 synced, 1 failed, 0 still pending.", output, StringComparison.Ordinal);
        Assert.Contains("The scan time is in the future.", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans waiting for upload (oldest first): 0", output, StringComparison.Ordinal);
        Assert.Contains("Deleted scan::", output, StringComparison.Ordinal);
        Assert.Contains("Goodbye", output, StringComparison.Ordinal);
        Assert.Single(_repository.ListAll());
    }

    [Fact]
    public async Task The_listener_log_line_follows_the_action_that_caused_it()
    {
        var output = await Run("n", "1", "APX-30000001", "1", "Riverside Depot", "", "", "6");

        var saved = output.IndexOf("Saved locally as Pending", StringComparison.Ordinal);
        var logged = output.IndexOf("Scan documents changed: scan::", StringComparison.Ordinal);
        var nextMenu = output.IndexOf("Choose 1-6", saved, StringComparison.Ordinal);
        Assert.True(saved >= 0 && logged > saved && nextMenu > logged, output);
        Assert.Contains("pending scans: 1", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans (live): 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_header_shows_the_couriers_scans_by_type()
    {
        foreach (var scan in SampleScans.Create("CR-001", Now)) _repository.Create(scan);

        var output = await Run("6");

        Assert.Contains("CR-001 scans by type: Pickup 2  InTransit 1  Delivered 1  Failed 1", output, StringComparison.Ordinal);
        Assert.Contains("Pending scans (live): 5", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sample_data_is_offered_on_an_empty_device()
    {
        var output = await Run("y", "3", "6");

        Assert.Contains("Pending scans waiting for upload (oldest first): 5", output, StringComparison.Ordinal);
        Assert.All(_repository.ListAll(), scan => Assert.Equal("CR-001", scan.CourierId));
    }

    [Theory]
    [InlineData("APX-1", "1", "Depot", "", "", "Not saved: A tracking number must be APX- followed by eight digits.")]
    [InlineData("APX-30000001", "9", "", "", "", "Not saved: choose a scan type from 1 to 4.")]
    [InlineData("APX-30000001", "1", "Depot", "", "tomorrow", "Not saved: the time must look like yyyy-MM-dd HH:mm.")]
    public async Task Bad_input_is_explained_and_nothing_is_saved(string tracking, string type, string location, string notes, string time, string message)
    {
        string[] answers = type == "9" ? ["n", "1", tracking, type, "6"] : ["n", "1", tracking, type, location, notes, time, "6"];

        var output = await Run(answers);

        Assert.Contains(message, output, StringComparison.Ordinal);
        Assert.Empty(_repository.ListAll());
    }

    [Fact]
    public async Task Deleting_an_unknown_row_is_explained()
    {
        var output = await Run("n", "5", "7", "6");

        Assert.Contains("No such row or scan ID", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task End_of_input_closes_the_session()
    {
        var output = await Run("n");

        Assert.Contains("Goodbye", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_default_to_courier_cr_001_and_the_local_app_data_folder()
    {
        var defaults = HarnessOptions.Parse([]);
        var custom = HarnessOptions.Parse(["--data", "courier-data", "--courier", " cr-007 "]);

        Assert.Equal("CR-001", defaults.CourierId);
        Assert.EndsWith(Path.Combine("ApexPost", "CourierOffline"), defaults.DataDirectory, StringComparison.Ordinal);
        Assert.Equal("CR-007", custom.CourierId);
        Assert.Equal(Path.GetFullPath("courier-data"), custom.DataDirectory);
        Assert.Throws<ArgumentException>(() => HarnessOptions.Parse(["--data"]));
        Assert.Throws<ArgumentException>(() => HarnessOptions.Parse(["--colour", "blue"]));
    }

    private async Task<string> Run(params string[] answers)
    {
        var text = new StringWriter();
        var writer = TextWriter.Synchronized(text);
        using var monitor = new ScanChangeMonitor(_temporary.Database, new LineLogger<ScanChangeMonitor>(writer));
        var server = new FakeShipmentUploader(new FixedTimeProvider(Now));
        var outbox = new OutboxService(_repository, server, logger: new LineLogger<OutboxService>(writer));
        var console = new CourierConsole(
            new StringReader(string.Join(Environment.NewLine, answers)), writer, _repository, monitor, outbox, server, "CR-001", new FixedTimeProvider(Now));

        await console.RunAsync();
        return text.ToString();
    }
}
