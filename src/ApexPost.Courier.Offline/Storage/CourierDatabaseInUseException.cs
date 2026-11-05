namespace ApexPost.Courier.Offline.Storage;

/// <summary>Another process (or another open <see cref="CourierDatabase"/>) already has this database folder.</summary>
public sealed class CourierDatabaseInUseException(string directory, Exception innerException)
    : InvalidOperationException($"The courier database in '{directory}' is already open in another process. Close it there first.", innerException)
{
    public string Directory { get; } = directory;
}
