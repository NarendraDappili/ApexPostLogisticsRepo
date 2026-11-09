using System.Globalization;
using ApexPost.Client;

namespace ApexPost.Client.Consumer;

/// <summary>
/// The consumer's walk through the API with the generated client: create a shipment, fetch it, upload two courier
/// scans, fetch it again to see the new status and latest scan, and ask for a shipment that does not exist, handling
/// the 404 through the generated exception type. Every step writes what it did.
/// </summary>
public sealed class ShipmentTour(IApexPostClient client, TimeProvider clock)
{
    /// <summary>A well-formed tracking number that is never issued, for the 404 step.</summary>
    public const string UnknownTrackingNumber = "APX-00000000";

    /// <summary>The courier in appsettings.json.</summary>
    public const string DefaultCourierId = "CR-1042";

    /// <summary>Runs the tour and returns the process exit code: 0 when every step behaved as the API documents.</summary>
    /// <param name="output">Where each step is reported.</param>
    /// <param name="trackingNumber">The new shipment's number; a random fictional one by default.</param>
    /// <param name="courierId">The courier the scans are recorded under: the courierId of the token the client sends.</param>
    /// <param name="cancellationToken">Stops the tour.</param>
    public async Task<int> RunAsync(TextWriter output, string? trackingNumber = null, string courierId = DefaultCourierId, CancellationToken cancellationToken = default)
    {
        trackingNumber ??= $"APX-9{Random.Shared.Next(0, 10_000_000):D7}";
        try
        {
            // 1. Create.
            var created = await client.CreateShipmentAsync(new CreateShipmentRequest
            {
                TrackingNumber = trackingNumber,
                RecipientName = "Kofi Boateng",
                RecipientPhone = "+44 20 7946 0123",
                RecipientAddress = "8 Quay Street",
                City = "Portside",
                WeightKg = 1.75m,
            }, cancellationToken);
            await output.WriteLineAsync($"Created {created.TrackingNumber}: {created.Status}, {Kilograms(created.WeightKg)} kg, for {created.Recipient.Name} in {created.Recipient.City}.");

            // 2. Fetch.
            var fetched = await client.GetShipmentAsync(trackingNumber, cancellationToken);
            await output.WriteLineAsync($"Fetched {fetched.TrackingNumber}: {fetched.Status}, latest scan: {Describe(fetched.LatestScan)}.");

            // 3. Upload two scans, as the courier's device would after working offline.
            var now = clock.GetUtcNow();
            var upload = await client.UploadScansAsync(trackingNumber, new ScanUploadRequest
            {
                Scans =
                [
                    Scan(TrackingScanType.Pickup, "Portside depot", now.AddHours(-2), courierId),
                    Scan(TrackingScanType.InTransit, "Northgate hub", now.AddHours(-1), courierId),
                ],
            }, cancellationToken);
            await output.WriteLineAsync($"Uploaded 2 scans: {upload.Accepted} accepted, {upload.DuplicatesSkipped} duplicates skipped, {upload.Rejected} rejected; the shipment is now {upload.ShipmentStatus}.");

            var moved = await client.GetShipmentAsync(trackingNumber, cancellationToken);
            await output.WriteLineAsync($"Fetched {moved.TrackingNumber}: {moved.Status}, latest scan: {Describe(moved.LatestScan)}.");

            // 4. A shipment that does not exist: the client throws its generated exception, with the API's ProblemDetails.
            try
            {
                await client.GetShipmentAsync(UnknownTrackingNumber, cancellationToken);
                await output.WriteLineAsync($"Unexpected: {UnknownTrackingNumber} exists.");
                return 1;
            }
            catch (ApexPostApiException<ProblemDetails> notFound) when (notFound.IsNotFound)
            {
                await output.WriteLineAsync($"Not found, as expected ({notFound.StatusCode}): {notFound.Result.Title} {notFound.Result.Detail}");
            }

            return 0;
        }
        catch (ApexPostApiException failure)
        {
            await output.WriteLineAsync($"The API answered {failure.StatusCode}: {failure.Response}");
            return 1;
        }
        catch (HttpRequestException unreachable)
        {
            await output.WriteLineAsync($"The API could not be reached: {unreachable.Message} Is it running?");
            return 1;
        }
    }

    private static ScanUploadItem Scan(TrackingScanType type, string location, DateTimeOffset scannedAtUtc, string courierId) => new()
    {
        ScanId = $"scan::{Guid.NewGuid()}",
        ScanType = type,
        Location = location,
        CourierId = courierId,
        ScannedAtUtc = scannedAtUtc,
    };

    private static string Describe(LatestScanResponse? scan) =>
        scan is null ? "none yet" : $"{scan.ScanType} at {scan.Location}, {scan.ScannedAtUtc.ToString("u", CultureInfo.InvariantCulture)}";

    private static string Kilograms(decimal weight) => weight.ToString(CultureInfo.InvariantCulture);
}
