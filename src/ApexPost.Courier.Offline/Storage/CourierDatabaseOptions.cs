namespace ApexPost.Courier.Offline.Storage;

/// <summary>Where the courier device keeps its local database.</summary>
public sealed class CourierDatabaseOptions
{
    /// <summary>
    /// The folder that holds the <c>apexpost_courier.cblite2</c> database. Each process needs its own folder: an
    /// embedded database is opened by one process at a time (OFFLINE-DESIGN.md).
    /// </summary>
    public required string Directory { get; init; }
}
