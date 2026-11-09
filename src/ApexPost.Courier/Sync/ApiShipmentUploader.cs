using System.Net;
using System.Text.RegularExpressions;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Sync;

/// <summary>
/// The real uploader that replaces Part A's stand-in server: the outbox's batch of up to 20 scans goes to the v2 API,
/// one request per shipment (the API takes scans per tracking number), and the answer becomes one outcome per scan.
/// <list type="bullet">
/// <item>A scan the API listed in rejections is Rejected with its reason; every other scan it has is Accepted (stored
/// now or before: the API reports duplicates only as a count, and both mean the server has the scan).</item>
/// <item>404 (the shipment does not exist) rejects the shipment's scans; 400 rejects the scans its errors name. Both
/// would get the same answer every time, so retrying them would block the outbox for good.</item>
/// <item>403 and 409 leave the scans Pending (no outcome) for a later sync: another courier's scans, or a parallel
/// upload. Only the signed-in courier's scans are sent at all.</item>
/// <item>Anything that says the server or the sign-in is the problem (no answer, 401, 408, 5xx) stops the batch: the
/// outbox keeps every scan Pending, and sending them again later is safe (RESILIENCE.md).</item>
/// </list>
/// </summary>
internal sealed partial class ApiShipmentUploader(ApexApiClient api, CourierSession session, ILogger<ApiShipmentUploader> logger) : IShipmentUploader
{
    public async Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> scans, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<ScanUploadOutcome>(scans.Count);
        foreach (var group in scans.GroupBy(scan => (scan.CourierId, scan.TrackingNumber)))
        {
            var batch = group.ToList();
            if (!string.Equals(group.Key.CourierId, session.CourierId, StringComparison.Ordinal))
            {
                // The API takes scans only under the token's own courier ID (SECURITY.md); these wait for their courier.
                logger.LogInformation("{Count} scan(s) of courier {CourierId} wait until that courier signs in", batch.Count, group.Key.CourierId);
                continue;
            }

            try
            {
                var result = await api.UploadScansAsync(group.Key.TrackingNumber, batch, cancellationToken);
                var rejected = (result.Rejections ?? [])
                    .Where(rejection => rejection.ScanId is not null)
                    .GroupBy(rejection => rejection.ScanId, StringComparer.Ordinal)
                    .ToDictionary(rejection => rejection.Key, rejection => rejection.First().Reason, StringComparer.Ordinal);
                outcomes.AddRange(batch.Select(scan => rejected.TryGetValue(scan.Id.Value, out var reason)
                    ? new ScanUploadOutcome(scan.Id, ScanUploadStatus.Rejected, string.IsNullOrWhiteSpace(reason) ? "Rejected by the server." : reason)
                    : new ScanUploadOutcome(scan.Id, ScanUploadStatus.Accepted)));
            }
            catch (ApexApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                outcomes.AddRange(batch.Select(scan => new ScanUploadOutcome(scan.Id, ScanUploadStatus.Rejected, exception.Message)));
            }
            catch (ApexApiException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
            {
                outcomes.AddRange(RejectNamedScans(batch, exception));
            }
            catch (ApexApiException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict || (int)exception.StatusCode < 300)
            {
                // Not the scans' fault, and not a reason to stop: they stay Pending for a later sync.
                logger.LogWarning("{Count} scan(s) for {TrackingNumber} stay Pending: {Reason}", batch.Count, group.Key.TrackingNumber, exception.Message);
            }
        }

        return outcomes;
    }

    /// <summary>
    /// A 400 names the refused scans by their place in the request (scans[2].scannedAtUtc). Those are rejected with
    /// their messages; the others get no outcome, stay Pending and go alone next time. If no scan is named, the
    /// whole request was refused and every scan in it is rejected.
    /// </summary>
    private static IEnumerable<ScanUploadOutcome> RejectNamedScans(List<ShipmentScan> batch, ApexApiException exception)
    {
        var reasons = new Dictionary<int, List<string>>();
        foreach (var (key, messages) in exception.Problem?.Errors ?? [])
        {
            var match = ScanErrorKey().Match(key);
            if (!match.Success || !int.TryParse(match.Groups["index"].Value, out var index) || index >= batch.Count) continue;
            if (!reasons.TryGetValue(index, out var list)) reasons[index] = list = [];
            list.Add($"{match.Groups["field"].Value}: {string.Join(" ", messages ?? [])}".Trim());
        }

        return reasons.Count == 0
            ? batch.Select(scan => new ScanUploadOutcome(scan.Id, ScanUploadStatus.Rejected, exception.Message))
            : reasons.Select(pair => new ScanUploadOutcome(batch[pair.Key].Id, ScanUploadStatus.Rejected, string.Join("; ", pair.Value)));
    }

    [GeneratedRegex(@"^scans\[(?<index>\d+)\]\.?(?<field>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ScanErrorKey();
}
