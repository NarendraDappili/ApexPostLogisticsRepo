namespace ApexPost.Courier.Offline.Sync;

public sealed class OutboxOptions
{
    /// <summary>How many pending scans go to the server in one upload call (Part D uploads in batches of 20).</summary>
    public int BatchSize { get; init; } = 20;
}
