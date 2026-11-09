using ApexPost.Courier.Api;
using ApexPost.Courier.Sync;

namespace ApexPost.Courier.Screens;

/// <summary>
/// Sync Status: the last sync, the last time the server was reached, the circuit breaker's state and the latest
/// failures.
/// </summary>
internal sealed class SyncStatusScreen(ConsoleScreen screen, SyncHistory history, ServerAvailability server)
{
    public void Run()
    {
        var last = history.LastRun;
        screen.WriteLine("Sync status");
        screen.WriteLine($"  Last sync:            {(last is null ? "never in this session" : $"{ConsoleScreen.Time(last.FinishedAtUtc)} UTC, {Describe(last)}")}");
        screen.WriteLine($"  Server last reached:  {(history.LastReachedServerAtUtc is { } reached ? $"{ConsoleScreen.Time(reached)} UTC" : "never in this session")}");
        screen.WriteLine($"  Connection (circuit): {Describe(server)}");

        var failures = history.RecentFailures;
        screen.WriteLine($"  Recent failures (newest first): {failures.Count}");
        foreach (var failure in failures) screen.WriteLine($"    {ConsoleScreen.Time(failure.AtUtc)}  {failure.What}  {failure.Reason}");
    }

    private static string Describe(SyncRun run) => run.ReachedServer
        ? $"{run.Result.Synced} synced, {run.Result.Failed} failed, {run.Result.LeftPending} still pending"
        : $"server not reached, {run.Result.LeftPending} still pending";

    private static string Describe(ServerAvailability server) => server.State switch
    {
        ConnectionState.Open => $"open since {ConsoleScreen.Time(server.OpenedAtUtc!.Value)} UTC: {ServerAvailability.Banner}. The next try is after {server.RetryAfterUtc!.Value.UtcDateTime:HH:mm:ss} UTC.",
        ConnectionState.HalfOpen => $"half-open: the next call tries the server again. {ServerAvailability.Banner} until it answers.",
        _ => "closed: calls go to the server",
    };
}
