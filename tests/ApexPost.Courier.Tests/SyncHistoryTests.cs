using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Courier.Sync;

namespace ApexPost.Courier.Tests;

public sealed class SyncHistoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_run_that_reached_the_server_sets_the_last_reached_time_and_keeps_its_rejections()
    {
        var history = new SyncHistory();
        var rejected = ScanId.New();

        history.Record(new SyncRun(Noon, new OutboxSyncResult(2, 1, 1, 0, [new ScanUploadOutcome(rejected, ScanUploadStatus.Rejected, "The scan time is in the future.")], null)));

        Assert.Equal(Noon, history.LastReachedServerAtUtc);
        var failure = Assert.Single(history.RecentFailures);
        Assert.Equal((rejected.ToString(), "The scan time is in the future."), (failure.What, failure.Reason));
    }

    [Fact]
    public void A_run_that_did_not_reach_the_server_is_a_failure_and_keeps_the_last_reached_time()
    {
        var history = new SyncHistory();
        history.Record(new SyncRun(Noon, new OutboxSyncResult(0, 0, 0, 0, [], null)));

        history.Record(new SyncRun(Noon.AddMinutes(5), new OutboxSyncResult(0, 0, 0, 3, [], "No network.")));

        Assert.Equal(Noon, history.LastReachedServerAtUtc);
        Assert.Equal(Noon.AddMinutes(5), history.LastRun!.FinishedAtUtc);
        Assert.False(history.LastRun.ReachedServer);
        Assert.Equal(("Upload", "No network."), (history.RecentFailures[0].What, history.RecentFailures[0].Reason));
    }

    [Fact]
    public void Only_the_newest_failures_are_kept()
    {
        var history = new SyncHistory();

        for (var minute = 0; minute < SyncHistory.KeptFailures + 5; minute++)
        {
            history.Record(new SyncRun(Noon.AddMinutes(minute), new OutboxSyncResult(0, 0, 0, 1, [], $"Failure {minute}")));
        }

        Assert.Equal(SyncHistory.KeptFailures, history.RecentFailures.Count);
        Assert.Equal($"Failure {SyncHistory.KeptFailures + 4}", history.RecentFailures[0].Reason);
        Assert.Equal("Failure 5", history.RecentFailures[^1].Reason);
    }
}
