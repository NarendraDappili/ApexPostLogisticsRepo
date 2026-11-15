using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Specs.Support;

// State shared between the steps of one scenario goes through Reqnroll's context injection: each class below is
// created once per scenario and handed to every step class that asks for it in its constructor. No static fields.

/// <summary>This feature's head office, started by the hooks and kept in Reqnroll's <see cref="FeatureContext"/>.</summary>
public sealed class HeadOfficeContext(FeatureContext feature)
{
    public HeadOffice Host => feature.Get<HeadOffice>();
}

/// <summary>What the current scenario is about: the parcel in play and the last answers it got.</summary>
public sealed class ScenarioState
{
    /// <summary>The parcel the scenario's steps call "the shipment" or "that parcel".</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Who is signed in at head office in this scenario.</summary>
    public HttpClient? Client { get; set; }

    public HttpResponseMessage? LastResponse { get; set; }

    public ShipmentScan? LastScan { get; set; }

    public ScanUploadResult? LastUpload { get; set; }

    public string Parcel => TrackingNumber ?? throw new InvalidOperationException("No parcel is in play yet: a Given step must name one first.");
}

/// <summary>The courier's device for this scenario, given and wiped by the hooks.</summary>
public sealed class DeviceContext : IDisposable
{
    private CourierDevice? _device;

    public CourierDevice Device => _device ?? throw new InvalidOperationException("This scenario has no courier device: tag the feature @courier.");

    public void Start(HeadOffice headOffice) => _device = new CourierDevice(headOffice);

    public void Dispose()
    {
        _device?.Dispose();
        _device = null;
    }
}
