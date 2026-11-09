using ApexPost.Api.Contracts;
using ApexPost.Application.Tracking;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>A shipment's scans: the history here, and the courier upload (step B6).</summary>
[ApiController]
[Route("api/shipments/{trackingNumber:trackingNumber}/scans")]
[Produces("application/json")]
public sealed class ScansController(ShipmentTrackingService tracking) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScanResponse>>> GetScanHistory(string trackingNumber, CancellationToken cancellationToken)
    {
        var scans = await tracking.GetScanHistoryAsync(trackingNumber, cancellationToken);
        return Ok(scans.Select(ScanResponse.From).ToList());
    }
}
