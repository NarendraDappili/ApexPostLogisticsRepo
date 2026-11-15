using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Recording;

namespace ApexPost.UnitTests.CourierOffline.Recording;

/// <summary>
/// Week 5 (S7), built test-first (TDD-LOG.md). The double-scan guard: if the same courier records a scan of the same
/// type for the same parcel less than two minutes from an earlier one, the device keeps the first scan, saves nothing
/// new and says it was already recorded.
/// <para>
/// Why: a double-tapped Delivered scan creates two scans with different IDs. The server accepts the first and refuses
/// the second, because the shipment is already Delivered, so the courier sees a Failed scan for a parcel they just
/// delivered. Scan-ID idempotency cannot catch this, because the IDs differ.
/// </para>
/// </summary>
public sealed class ScanRecorderTests
{
    private const string Parcel = "APX-30000001";
    private const string Courier = "CR-001";

    private readonly Mock<IScanRepository> _scans = new();
    private readonly FakeTimeProvider _clock = new(TestTime.Now);

    public ScanRecorderTests() => _scans.Setup(scans => scans.FindByTrackingNumber(It.IsAny<string>())).Returns([]);

    // Characterisation: what the screen did before the rule, kept by the extracted recorder.
    [Fact]
    public void Record_FirstScanOfTheParcel_SavesItAsPending()
    {
        // Act
        var result = Recorder().Record(Parcel, ScanType.Delivered, Courier, "14 Elm Street", TestTime.Now);

        // Assert
        Assert.False(result.AlreadyRecorded);
        Assert.Equal((Parcel, ScanType.Delivered, SyncStatus.Pending), (result.Scan.TrackingNumber, result.Scan.ScanType, result.Scan.SyncStatus));
        _scans.Verify(scans => scans.Create(result.Scan), Times.Once);
    }

    // Characterisation: a scan without a time is dated by the injected clock, never the system clock.
    [Fact]
    public void Record_NoTimeGiven_DatesTheScanWithTheClock()
    {
        // Arrange
        _clock.Advance(TimeSpan.FromMinutes(7));

        // Act
        var result = Recorder().Record(Parcel, ScanType.Pickup, Courier, "Riverside Depot");

        // Assert
        Assert.Equal(TestTime.Now.AddMinutes(7), result.Scan.ScannedAtUtc);
    }

    [Theory]
    [InlineData(0)]      // a double tap
    [InlineData(1)]
    [InlineData(119)]    // just inside two minutes
    [InlineData(-30)]    // a typed time just before the earlier scan: the same event
    public void Record_SameScanAgainWithinTwoMinutes_ReturnsTheEarlierScanAndSavesNothing(int secondsLater)
    {
        // Arrange
        var earlier = Stored(ScanType.Delivered, Courier, TestTime.Now);

        // Act
        var result = Recorder().Record(Parcel, ScanType.Delivered, Courier, "14 Elm Street", TestTime.Now.AddSeconds(secondsLater));

        // Assert
        Assert.True(result.AlreadyRecorded);
        Assert.Same(earlier, result.Scan);
        _scans.Verify(scans => scans.Create(It.IsAny<ShipmentScan>()), Times.Never);
    }

    [Theory]
    [InlineData(120)]    // exactly two minutes: a new event
    [InlineData(600)]
    public void Record_SameScanTwoMinutesOrMoreLater_SavesASecondScan(int secondsLater)
    {
        // Arrange
        Stored(ScanType.Delivered, Courier, TestTime.Now);

        // Act
        var result = Recorder().Record(Parcel, ScanType.Delivered, Courier, "14 Elm Street", TestTime.Now.AddSeconds(secondsLater));

        // Assert
        Assert.False(result.AlreadyRecorded);
        _scans.Verify(scans => scans.Create(It.Is<ShipmentScan>(scan => scan.ScannedAtUtc == TestTime.Now.AddSeconds(secondsLater))), Times.Once);
    }

    [Theory]
    [InlineData("another scan type")]
    [InlineData("another courier")]
    public void Record_SoonAfterAScanThatDiffers_SavesASecondScan(string difference)
    {
        // Arrange
        Stored(difference == "another scan type" ? ScanType.InTransit : ScanType.Delivered, difference == "another courier" ? "CR-002" : Courier, TestTime.Now);

        // Act
        var result = Recorder().Record(Parcel, ScanType.Delivered, Courier, "14 Elm Street", TestTime.Now.AddSeconds(10));

        // Assert
        Assert.False(result.AlreadyRecorded);
        _scans.Verify(scans => scans.Create(It.IsAny<ShipmentScan>()), Times.Once);
    }

    [Fact]
    public void Record_SoonAfterAScanTheServerRefused_SavesANewScanSoTheCourierCanTryAgain()
    {
        // Arrange
        var refused = Stored(ScanType.Delivered, Courier, TestTime.Now);
        refused.MarkFailed();

        // Act
        var result = Recorder().Record(Parcel, ScanType.Delivered, Courier, "14 Elm Street", TestTime.Now.AddSeconds(30));

        // Assert
        Assert.False(result.AlreadyRecorded);
        _scans.Verify(scans => scans.Create(It.IsAny<ShipmentScan>()), Times.Once);
    }

    private ScanRecorder Recorder() => new(_scans.Object, _clock);

    /// <summary>A scan already on the device, returned in the parcel's history.</summary>
    private ShipmentScan Stored(ScanType scanType, string courierId, DateTimeOffset scannedAtUtc)
    {
        var scan = ShipmentScan.Record(Parcel, scanType, courierId, "14 Elm Street", scannedAtUtc);
        _scans.Setup(scans => scans.FindByTrackingNumber(Parcel)).Returns([scan]);
        return scan;
    }
}
