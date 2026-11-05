using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Sync;

/// <summary>
/// A stand-in for the ApexPost API until Part D. It behaves like the planned server: a scan ID it has already
/// stored comes back as <see cref="ScanUploadStatus.Duplicate"/>, a scan time in the future is rejected, and
/// <see cref="IsOffline"/> makes the whole call fail as a lost network connection would.
/// </summary>
public sealed class FakeShipmentUploader : IShipmentUploader
{
    /// <summary>How far ahead of the server clock a device clock may run before a scan time counts as future.</summary>
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _clock;
    private readonly HashSet<ScanId> _stored = [];
    private readonly List<IReadOnlyList<ScanId>> _calls = [];
    private readonly Lock _gate = new();

    public FakeShipmentUploader(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>When true, every upload fails with <see cref="HttpRequestException"/>.</summary>
    public bool IsOffline { get; set; }

    /// <summary>An extra server-side rule for tests and demos: return a reason to reject the scan, or null.</summary>
    public Func<ShipmentScan, string?>? RejectWhen { get; set; }

    /// <summary>The scan IDs sent in each upload call, in order.</summary>
    public IReadOnlyList<IReadOnlyList<ScanId>> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    public Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> scans, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scans);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOffline) throw new HttpRequestException("The ApexPost server cannot be reached (simulated: no network).");

        var latestAllowed = _clock.GetUtcNow() + AllowedClockSkew;
        var outcomes = new List<ScanUploadOutcome>(scans.Count);
        lock (_gate)
        {
            _calls.Add(scans.Select(scan => scan.Id).ToList());
            foreach (var scan in scans)
            {
                var reason = scan.ScannedAtUtc > latestAllowed ? "The scan time is in the future." : RejectWhen?.Invoke(scan);
                if (reason is not null) outcomes.Add(new ScanUploadOutcome(scan.Id, ScanUploadStatus.Rejected, reason));
                else if (_stored.Add(scan.Id)) outcomes.Add(new ScanUploadOutcome(scan.Id, ScanUploadStatus.Accepted));
                else outcomes.Add(new ScanUploadOutcome(scan.Id, ScanUploadStatus.Duplicate));
            }
        }

        return Task.FromResult<IReadOnlyList<ScanUploadOutcome>>(outcomes);
    }
}
