using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Shipping;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Infrastructure.Simulation;

/// <summary>
/// Simulated identity provider. A document is verified when its type is supported and its number is 6-20 letters or
/// digits; numbers ending in "0000" simulate a provider rejection so the Failed path can be demonstrated.
/// </summary>
public sealed class SimulatedIdentityVerifier : IIdentityVerifier
{
    private static readonly string[] DocumentTypes = ["Passport", "Driving licence", "National ID card"];

    public IReadOnlyCollection<string> SupportedDocumentTypes => DocumentTypes;

    public Task<IdentityVerificationOutcome> VerifyAsync(string documentType, string documentNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verified = DocumentTypes.Contains(documentType, StringComparer.OrdinalIgnoreCase)
            && documentNumber.Length is >= 6 and <= 20
            && documentNumber.All(char.IsAsciiLetterOrDigit)
            && !documentNumber.EndsWith("0000", StringComparison.Ordinal);
        return Task.FromResult(verified ? IdentityVerificationOutcome.Verified : IdentityVerificationOutcome.Failed);
    }
}

public sealed class SimulatedCourierOptions
{
    public int LatencyMilliseconds { get; set; } = 150;

    public int MaxConcurrentLookups { get; set; } = 4;
}

/// <summary>
/// Simulated courier tracking service with network-like latency. A SemaphoreSlim bounds how many lookups run at
/// once, so a burst of partner requests queues here instead of opening unbounded parallel calls to the courier.
/// </summary>
public sealed class SimulatedCourierTrackingClient : ICourierTrackingClient, IDisposable
{
    private static readonly string[] Statuses = ["Accepted at counter", "In transit", "Out for delivery", "Delivered"];
    private readonly SemaphoreSlim _concurrency;
    private readonly TimeSpan _latency;

    public SimulatedCourierTrackingClient(SimulatedCourierOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.LatencyMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentLookups, 1);
        _latency = TimeSpan.FromMilliseconds(options.LatencyMilliseconds);
        _concurrency = new SemaphoreSlim(options.MaxConcurrentLookups, options.MaxConcurrentLookups);
    }

    public async Task<string> GetStatusAsync(ShipmentId shipmentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shipmentId);
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);
            // A stable (non-randomized) hash so the same shipment always reports the same simulated status.
            return Statuses[shipmentId.Value.Sum(character => character) % Statuses.Length];
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public void Dispose() => _concurrency.Dispose();
}
