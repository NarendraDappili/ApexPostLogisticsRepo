using System.Net.Mime;
using ApexPost.Api.Contracts;
using ApexPost.Api.Versioning;
using ApexPost.Application.Tracking;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers.V1;

/// <summary>
/// A shipment's scans: the history, and the bulk upload of a courier's offline scans. Documented the same way as
/// <see cref="ShipmentsController"/>.
/// </summary>
[ApiController]
[ApiVersion(ApiVersions.V1, Deprecated = true)]
[Route("api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber}/scans")]
[Tags("Scans")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, MediaTypeNames.Application.ProblemJson)]
public sealed class ScansController(ShipmentTrackingService tracking) : ControllerBase
{
    /// <remarks>Every scan recorded for the shipment, oldest first.</remarks>
    /// <param name="trackingNumber" example="APX-24000001">The shipment's tracking number: APX- and eight digits.</param>
    /// <response code="200">The scans, oldest first; empty if the shipment has none yet.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpGet(Name = "GetScanHistory")]
    [EndpointSummary("Get a shipment's scan history")]
    [ProducesResponseType<IReadOnlyList<ScanResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<IReadOnlyList<ScanResponse>>> GetScanHistory(string trackingNumber, CancellationToken cancellationToken)
    {
        var scans = await tracking.GetScanHistoryAsync(trackingNumber, cancellationToken);
        return Ok(scans.Select(ScanResponse.From).ToList());
    }

    /// <remarks>
    /// Idempotent: each scan carries the ID its device gave it, so sending the same upload again stores nothing
    /// twice. New scans are applied oldest first and can move the shipment's status. The answer is 200 whenever the
    /// upload was processed, even if some scans were rejected: the response counts the accepted, already-stored and
    /// rejected scans and gives each rejection's reason.
    /// </remarks>
    /// <param name="trackingNumber" example="APX-24000001">The shipment's tracking number: APX- and eight digits.</param>
    /// <param name="request">One to 100 scans for this shipment.</param>
    /// <response code="200">The upload was processed; the counts say what happened to each scan.</response>
    /// <response code="400">A scan is malformed or repeated; the errors name it, for example scans[2].scannedAtUtc.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="409">Another upload for this shipment was saved at the same moment. Nothing was stored; send the upload again.</response>
    /// <response code="415">The body is not application/json.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpPost(Name = "UploadScans")]
    [Consumes(MediaTypeNames.Application.Json)]
    [EndpointSummary("Upload a courier's offline scans")]
    [ProducesResponseType<ScanUploadResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<ScanUploadResponse>> UploadScans(string trackingNumber, ScanUploadRequest request, CancellationToken cancellationToken) =>
        Ok(ScanUploadResponse.From(await tracking.UploadScansAsync(trackingNumber, request.ToApplication(), cancellationToken)));
}
