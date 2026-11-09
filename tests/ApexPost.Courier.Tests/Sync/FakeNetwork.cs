using ApexPost.Courier.Sync;

namespace ApexPost.Courier.Tests.Sync;

/// <summary>A network the test switches on and off, instead of the machine's real one (which differs per machine).</summary>
internal sealed class FakeNetwork : INetworkStatus
{
    public bool IsAvailable { get; set; } = true;

    public event EventHandler<bool>? AvailabilityChanged;

    /// <summary>Changes the state and raises the event, as the operating system would.</summary>
    public void Change(bool available)
    {
        IsAvailable = available;
        AvailabilityChanged?.Invoke(this, available);
    }
}
