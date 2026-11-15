using ApexPost.Api.Security;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Specs.Drivers;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.Steps;

/// <summary>Steps of ScanSync.feature: the courier's device on one side, head office on the other.</summary>
[Binding]
public sealed class ScanSyncSteps(DeviceContext device, HeadOfficeContext headOfficeContext, HeadOfficeDriver headOffice, ScenarioState state)
{
    // A fixed instant in the future for any run: head office must refuse it.
    private static readonly DateTimeOffset InTheFuture = new(2100, 1, 1, 9, 30, 0, TimeSpan.Zero);

    private CourierDevice Device => device.Device;

    private ShipmentScan Scan => state.LastScan ?? throw new InvalidOperationException("The courier has not recorded a scan yet.");

    [Given(@"^courier ""(.*)"" is signed in on their device$")]
    public async Task GivenACourierIsSignedInOnTheirDevice(string courierId)
    {
        Device.Session.SignIn(courierId);
        await Device.Tokens.SaveAsync(headOfficeContext.Host.TokenFor([ApiRoles.Courier], courierId));
    }

    [Given(@"^a shipment out for delivery$")]
    public async Task GivenAShipmentOutForDelivery()
    {
        state.TrackingNumber = headOffice.NextTrackingNumber();
        await headOffice.RegisterAtAsync(state.TrackingNumber, "OutForDelivery");
    }

    [Given(@"^a shipment that has just been registered$")]
    public async Task GivenAShipmentThatHasJustBeenRegistered()
    {
        state.TrackingNumber = headOffice.NextTrackingNumber();
        await headOffice.RegisterAtAsync(state.TrackingNumber, "Created");
    }

    [Given(@"^a parcel head office does not know$")]
    public void GivenAParcelHeadOfficeDoesNotKnow() => state.TrackingNumber = headOffice.NextTrackingNumber();

    [Given(@"^the device has no network$")]
    public void GivenTheDeviceHasNoNetwork() => Device.Network.Set(false);

    [Given(@"^the courier has recorded a ""(.*)"" scan of that parcel at ""(.*)""$")]
    [When(@"^the courier records a ""(.*)"" scan of that parcel at ""(.*)""$")]
    public void WhenTheCourierRecordsAScan(ScanType scanType, string location) => Record(scanType, location, scannedAtUtc: null);

    [When(@"^the courier records a ""(.*)"" scan of that parcel at ""(.*)"" dated (now|in the future)$")]
    public void WhenTheCourierRecordsADatedScan(ScanType scanType, string location, string when) =>
        Record(scanType, location, when == "in the future" ? InTheFuture : null);

    [Given(@"^the device has synced$")]
    [When(@"^the device tries to sync$")]
    [When(@"^the device syncs$")]
    public Task WhenTheDeviceSyncs() => Device.SyncAsync();

    [When(@"^the network returns and the device syncs$")]
    public Task WhenTheNetworkReturnsAndTheDeviceSyncs()
    {
        Device.Network.Set(true);
        return Device.SyncAsync();
    }

    [When(@"^the same scan reaches head office again$")]
    public async Task WhenTheSameScanReachesHeadOfficeAgain() =>
        // As when the device never heard head office's first answer and sends the scan once more.
        state.LastUpload = await Device.Api.UploadScansAsync(state.Parcel, [Scan]);

    [Then(@"^the scan is waiting on the device$")]
    public void ThenTheScanIsWaiting() => Assert.Equal(SyncStatus.Pending, StoredScan().SyncStatus);

    [Then(@"^the scan is no longer waiting on the device$")]
    public void ThenTheScanIsNoLongerWaiting() => Assert.Equal(SyncStatus.Synced, StoredScan().SyncStatus);

    [Then(@"^the scan is marked as failed on the device$")]
    public void ThenTheScanIsMarkedAsFailed() => Assert.Equal(SyncStatus.Failed, StoredScan().SyncStatus);

    [Then(@"^the courier is told ""(.*)""$")]
    public void ThenTheCourierIsTold(string reason) =>
        Assert.Contains(Device.History.RecentFailures, failure => failure.What == Scan.Id.ToString() && failure.Reason.Contains(reason, StringComparison.Ordinal));

    [Then(@"^head office has no scans for that parcel$")]
    public async Task ThenHeadOfficeHasNoScans() => Assert.Empty(await headOffice.ScansAsync(state.Parcel));

    [Then(@"^head office shows that parcel as ""(.*)"" with (\d+) scans?$")]
    public async Task ThenHeadOfficeShowsThatParcel(string status, int scans)
    {
        var shipment = await headOffice.LookUpAsync(state.Parcel) ?? throw new Xunit.Sdk.XunitException($"Head office does not know {state.Parcel}.");
        Assert.Equal(status, shipment.GetProperty("status").GetString());
        Assert.Equal(scans, (await headOffice.ScansAsync(state.Parcel)).Count);
    }

    [Then(@"^head office says it already had that scan$")]
    public void ThenHeadOfficeSaysItAlreadyHadThatScan()
    {
        var upload = state.LastUpload ?? throw new InvalidOperationException("Nothing was sent to head office in this step.");
        Assert.Equal((0, 1, 0), (upload.Accepted, upload.DuplicatesSkipped, upload.Rejected));
    }

    private void Record(ScanType scanType, string location, DateTimeOffset? scannedAtUtc)
    {
        var result = Device.Recorder.Record(state.Parcel, scanType, Device.Session.CourierId!, location, scannedAtUtc);
        Assert.False(result.AlreadyRecorded);
        state.LastScan = result.Scan;
    }

    private ShipmentScan StoredScan() => Device.Scans.GetById(Scan.Id) ?? throw new Xunit.Sdk.XunitException("The scan is no longer on the device.");
}
