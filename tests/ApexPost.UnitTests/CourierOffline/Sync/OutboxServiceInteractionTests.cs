using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.UnitTests.CourierOffline.Sync;

/// <summary>
/// Week 5 (S6), risk R4 ("a courier's proof of delivery is never lost"): the outbox isolated from its store and its
/// server with Moq. The tests check how the outbox talks to them:
/// <list type="bullet">
/// <item>batches of at most 20;</item>
/// <item>nothing marked when the upload fails;</item>
/// <item>a rejected scan marked Failed;</item>
/// <item>one run at a time.</item>
/// </list>
/// <para>
/// What these mocks do not prove: that <c>ListPending</c>'s SQL++ query really returns the Pending scans oldest first
/// through its index (ScanQueryTests, on Couchbase Lite), or that the server accepts these scans (ScanSync.feature).
/// OutboxServiceTests runs the same outbox on a real store.
/// </para>
/// </summary>
public sealed class OutboxServiceInteractionTests
{
    private readonly Mock<IScanRepository> _scans = new();
    private readonly Mock<IShipmentUploader> _uploader = new();

    [Fact]
    public async Task SyncPendingAsync_NothingPending_NeverCallsTheServer()
    {
        // Arrange
        _scans.Setup(scans => scans.ListPending()).Returns([]);

        // Act
        var result = await Outbox().SyncPendingAsync();

        // Assert
        Assert.Equal((0, 0, 0), (result.Attempted, result.Synced, result.LeftPending));
        _uploader.Verify(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_45PendingScans_SendsTwoBatchesOf20AndOneOf5AndMarksEachScanSyncedOnce()
    {
        // Arrange
        var pending = Pending(45);
        AcceptEverything();

        // Act
        var result = await Outbox().SyncPendingAsync();

        // Assert
        Assert.Equal((45, 45, 0), (result.Attempted, result.Synced, result.LeftPending));
        _uploader.Verify(uploader => uploader.UploadAsync(It.Is<IReadOnlyList<ShipmentScan>>(batch => batch.Count == 20), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _uploader.Verify(uploader => uploader.UploadAsync(It.Is<IReadOnlyList<ShipmentScan>>(batch => batch.Count == 5), It.IsAny<CancellationToken>()), Times.Once);
        _uploader.Verify(uploader => uploader.UploadAsync(It.Is<IReadOnlyList<ShipmentScan>>(batch => batch.Count > 20), It.IsAny<CancellationToken>()), Times.Never);
        foreach (var scan in pending)
        {
            _scans.Verify(scans => scans.Update(It.Is<ShipmentScan>(updated => updated.Id == scan.Id && updated.SyncStatus == SyncStatus.Synced)), Times.Once);
        }
    }

    [Fact]
    public async Task SyncPendingAsync_ServerUnreachable_StopsAfterTheFirstBatchAndLeavesEveryScanPending()
    {
        // Arrange
        var pending = Pending(45);
        _uploader.Setup(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No connection could be made."));

        // Act
        var result = await Outbox().SyncPendingAsync();

        // Assert
        Assert.Equal((0, 45, "No connection could be made."), (result.Synced, result.LeftPending, result.UploadError));
        _uploader.Verify(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Once);
        _scans.Verify(scans => scans.Update(It.IsAny<ShipmentScan>()), Times.Never);
        Assert.All(pending, scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
    }

    [Fact]
    public async Task SyncPendingAsync_ServerRejectsOneScan_MarksThatScanFailedWithTheReasonAndTheOthersSynced()
    {
        // Arrange
        var pending = Pending(3);
        var rejected = pending[1];
        _uploader.Setup(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShipmentScan> batch, CancellationToken _) => [.. batch.Select(scan => scan.Id == rejected.Id
                ? new ScanUploadOutcome(scan.Id, ScanUploadStatus.Rejected, "The shipment is already Delivered and cannot change.")
                : new ScanUploadOutcome(scan.Id, ScanUploadStatus.Accepted))]);

        // Act
        var result = await Outbox().SyncPendingAsync();

        // Assert
        Assert.Equal((2, 1), (result.Synced, result.Failed));
        Assert.Equal("The shipment is already Delivered and cannot change.", Assert.Single(result.Rejections).Reason);
        _scans.Verify(scans => scans.Update(It.Is<ShipmentScan>(scan => scan.Id == rejected.Id && scan.SyncStatus == SyncStatus.Failed)), Times.Once);
        _scans.Verify(scans => scans.Update(It.Is<ShipmentScan>(scan => scan.SyncStatus == SyncStatus.Synced)), Times.Exactly(2));
    }

    [Fact]
    public async Task SyncPendingAsync_CalledWhileARunIsInProgress_ReturnsAlreadyRunningAndUploadsOnlyOnce()
    {
        // Arrange: the first run's upload waits until the test lets it finish.
        Pending(2);
        var firstUpload = new TaskCompletionSource<IReadOnlyList<ScanUploadOutcome>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uploader.Setup(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>())).Returns(firstUpload.Task);
        var outbox = Outbox();
        var firstRun = outbox.SyncPendingAsync();

        // Act
        var secondRun = await outbox.SyncPendingAsync();

        // Assert
        Assert.True(secondRun.AlreadyRunning);
        firstUpload.SetResult([]);
        Assert.False((await firstRun).AlreadyRunning);
        _uploader.Verify(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private OutboxService Outbox() => new(_scans.Object, _uploader.Object, new OutboxOptions());

    /// <summary>Stores <paramref name="count"/> Pending scans in the mocked repository, findable by ID.</summary>
    private List<ShipmentScan> Pending(int count)
    {
        List<ShipmentScan> scans = [.. Enumerable.Range(0, count).Select(index =>
            ShipmentScan.Record($"APX-{30000000 + index:D8}", ScanType.Pickup, "CR-001", "Riverside Depot", TestTime.Now.AddMinutes(-count + index)))];
        var byId = scans.ToDictionary(scan => scan.Id);
        _scans.Setup(repository => repository.ListPending()).Returns(scans);
        _scans.Setup(repository => repository.GetById(It.IsAny<ScanId>())).Returns((ScanId id) => byId.GetValueOrDefault(id));
        return scans;
    }

    private void AcceptEverything() =>
        _uploader.Setup(uploader => uploader.UploadAsync(It.IsAny<IReadOnlyList<ShipmentScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShipmentScan> batch, CancellationToken _) => [.. batch.Select(scan => new ScanUploadOutcome(scan.Id, ScanUploadStatus.Accepted))]);
}
