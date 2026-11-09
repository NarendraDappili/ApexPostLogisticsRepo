using System.Net.NetworkInformation;

namespace ApexPost.Courier.Sync;

/// <summary>
/// Whether the device has a network, and when that changes. It plays the part of MAUI's
/// Connectivity.ConnectivityChanged for the console app.
/// </summary>
internal interface INetworkStatus
{
    bool IsAvailable { get; }

    /// <summary>Raised with the new state each time the network comes or goes.</summary>
    event EventHandler<bool>? AvailabilityChanged;
}

/// <summary>
/// The operating system's view: NetworkChange.NetworkAvailabilityChanged, plus NetworkAddressChanged, which also
/// fires when Wi-Fi reconnects with a new address or airplane mode is turned off. "Available" means some interface
/// is up; whether the API answers is the circuit breaker's business.
/// </summary>
internal sealed class SystemNetworkStatus : INetworkStatus, IDisposable
{
    private readonly Lock _gate = new();
    private bool _available = NetworkInterface.GetIsNetworkAvailable();

    public SystemNetworkStatus()
    {
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    public event EventHandler<bool>? AvailabilityChanged;

    public bool IsAvailable
    {
        get
        {
            lock (_gate) return _available;
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => Update(args.IsAvailable);

    private void OnAddressChanged(object? sender, EventArgs args) => Update(NetworkInterface.GetIsNetworkAvailable());

    private void Update(bool available)
    {
        lock (_gate)
        {
            if (_available == available) return;
            _available = available;
        }

        AvailabilityChanged?.Invoke(this, available);
    }
}
