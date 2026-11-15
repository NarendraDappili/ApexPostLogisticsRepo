namespace ApexPost.Courier.Sync;

/// <summary>
/// Whether the device has a network, and when that changes. Each app head supplies its own. The console app reads
/// the operating system (<c>SystemNetworkStatus</c>); a MAUI app would wrap Connectivity.ConnectivityChanged.
/// </summary>
public interface INetworkStatus
{
    bool IsAvailable { get; }

    /// <summary>Raised with the new state each time the network comes or goes.</summary>
    event EventHandler<bool>? AvailabilityChanged;
}
