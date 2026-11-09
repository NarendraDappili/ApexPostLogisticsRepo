using System.Net;
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
/// The operating system's view, re-read on NetworkChange.NetworkAvailabilityChanged and NetworkAddressChanged (which
/// also fires when Wi-Fi reconnects or airplane mode is turned off). "Available" means an interface is up and has a
/// default gateway, a way out of this machine. NetworkInterface.GetIsNetworkAvailable() alone would count virtual
/// adapters (Hyper-V, WSL, Docker) that stay up in airplane mode. Whether the API answers is the circuit breaker's
/// business.
/// </summary>
internal sealed class SystemNetworkStatus : INetworkStatus, IDisposable
{
    private readonly Lock _gate = new();
    private bool _available = HasWayOut();

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

    /// <summary>Some interface that is up, not loopback or a tunnel, and has a default gateway.</summary>
    internal static bool HasWayOut()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up
                && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && adapter.GetIPProperties().GatewayAddresses.Any(gateway => !gateway.Address.Equals(IPAddress.Any) && !gateway.Address.Equals(IPAddress.IPv6Any)));
        }
        catch (NetworkInformationException)
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
    }

    // The event's own flag would count virtual adapters too, so the state is read again either way.
    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => Update(HasWayOut());

    private void OnAddressChanged(object? sender, EventArgs args) => Update(HasWayOut());

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
