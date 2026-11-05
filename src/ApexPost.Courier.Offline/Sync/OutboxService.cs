using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Courier.Offline.Sync;

/// <summary>
/// The outbox of the save-locally-first strategy: every scan is written to the device as Pending, and this service
/// later reads the pending scans, uploads them in batches and marks each one Synced or Failed from the server's
/// answer. If the upload itself fails (no network, server down), the scans stay Pending and the next run retries
/// them; that is safe because the server skips scan IDs it already has.
/// </summary>
public sealed class OutboxService
{
    private readonly IScanRepository _scans;
    private readonly IShipmentUploader _uploader;
    private readonly ILogger<OutboxService> _logger;
    private readonly int _batchSize;
    private readonly SemaphoreSlim _running = new(1, 1);

    public OutboxService(IScanRepository scans, IShipmentUploader uploader, OutboxOptions? options = null, ILogger<OutboxService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scans);
        ArgumentNullException.ThrowIfNull(uploader);
        options ??= new OutboxOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1, nameof(options));
        _scans = scans;
        _uploader = uploader;
        _batchSize = options.BatchSize;
        _logger = logger ?? NullLogger<OutboxService>.Instance;
    }

    /// <summary>
    /// Uploads every pending scan once. Runs never overlap: a call made while another is in progress (for example
    /// a manual sync during an automatic one) returns at once with <see cref="OutboxSyncResult.AlreadyRunning"/>.
    /// </summary>
    public async Task<OutboxSyncResult> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!await _running.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return OutboxSyncResult.SkippedBecauseRunning;

        try
        {
            var pending = _scans.ListPending();
            var tally = new Tally();
            string? uploadError = null;

            foreach (var batch in pending.Chunk(_batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<ScanUploadOutcome> outcomes;
                try
                {
                    outcomes = await _uploader.UploadAsync(batch, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // The whole call failed, so nothing in this batch is known to be on the server: keep everything
                    // Pending and stop, because the next batch would most likely fail the same way.
                    uploadError = exception.Message;
                    _logger.LogWarning(exception, "Scan upload failed; {Count} scans stay Pending for the next sync", pending.Count - tally.Attempted);
                    break;
                }

                tally.Attempted += batch.Length;
                Apply(batch, outcomes, tally);
            }

            var result = new OutboxSyncResult(tally.Attempted, tally.Synced, tally.Failed, pending.Count - tally.Synced - tally.Failed, tally.Rejections, uploadError);
            _logger.LogInformation(
                "Outbox run: {Attempted} sent, {Synced} synced, {Failed} failed, {LeftPending} still pending",
                result.Attempted, result.Synced, result.Failed, result.LeftPending);
            return result;
        }
        finally
        {
            _running.Release();
        }
    }

    private void Apply(ShipmentScan[] batch, IReadOnlyList<ScanUploadOutcome> outcomes, Tally tally)
    {
        var outcomeById = new Dictionary<ScanId, ScanUploadOutcome>();
        foreach (var outcome in outcomes) outcomeById[outcome.ScanId] = outcome;

        foreach (var sent in batch)
        {
            if (!outcomeById.TryGetValue(sent.Id, out var outcome))
            {
                _logger.LogWarning("The server gave no answer for scan {ScanId}; it stays Pending", sent.Id);
                continue;
            }

            // Mark the stored copy, not the one read before the upload: the courier may have corrected or deleted
            // the scan while the upload was in flight, and writing the old copy back would undo that.
            var current = _scans.GetById(sent.Id);
            if (current is null || current.SyncStatus != SyncStatus.Pending)
            {
                _logger.LogInformation("Scan {ScanId} was deleted or already marked during the upload; left as it is", sent.Id);
                continue;
            }

            if (current.Location != sent.Location || current.Notes != sent.Notes)
            {
                // The server judged the old text. Keep the corrected scan Pending so the next run sends it again; a
                // rejected scan is then judged afresh, an accepted one comes back as Duplicate (OFFLINE-DESIGN.md).
                _logger.LogInformation("Scan {ScanId} was corrected during the upload; it stays Pending", sent.Id);
                continue;
            }

            var rejected = outcome.Status == ScanUploadStatus.Rejected;
            if (rejected) current.MarkFailed();
            else current.MarkSynced();

            try
            {
                _scans.Update(current);
            }
            catch (Exception exception) when (exception is ScanNotFoundException or InvalidOperationException)
            {
                // Lost a race with the courier editing or deleting the scan; the next run looks at it again.
                _logger.LogInformation(exception, "Scan {ScanId} changed while it was being marked; it stays as it is", sent.Id);
                continue;
            }

            if (rejected)
            {
                tally.Failed++;
                tally.Rejections.Add(outcome);
                _logger.LogWarning("Server rejected scan {ScanId}: {Reason}", sent.Id, outcome.Reason);
            }
            else
            {
                tally.Synced++;
            }
        }
    }

    private sealed class Tally
    {
        public int Attempted { get; set; }
        public int Synced { get; set; }
        public int Failed { get; set; }
        public List<ScanUploadOutcome> Rejections { get; } = [];
    }
}
