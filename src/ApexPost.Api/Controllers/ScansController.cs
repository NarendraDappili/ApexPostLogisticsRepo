using ApexPost.Api.Contracts;
using ApexPost.Application.Tracking;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>A shipment's scans: the history, and the bulk upload of a courier's offline scans.</summary>
[ApiController]
[Route("api/shipments/{trackingNumber:trackingNumber}/scans")]
public sealed class ScansController(ShipmentTrackingService tracking) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScanResponse>>> GetScanHistory(string trackingNumber, CancellationToken cancellationToken)
    {
        var scans = await tracking.GetScanHistoryAsync(trackingNumber, cancellationToken);
        return Ok(scans.Select(ScanResponse.From).ToList());
    }

    /// <summary>
    /// Idempotent: the scans carry their device IDs, so sending the same upload again stores nothing twice. The answer
    /// is 200 whenever the upload was processed, even if some scans were rejected; the rejections say which and why.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ScanUploadResponse>> UploadScans(string trackingNumber, ScanUploadRequest request, CancellationToken cancellationToken) =>
        Ok(ScanUploadResponse.From(await tracking.UploadScansAsync(trackingNumber, request.ToApplication(), cancellationToken)));
}
