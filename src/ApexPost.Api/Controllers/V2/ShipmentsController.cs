using System.Net.Mime;
using ApexPost.Api.Contracts;
using ApexPost.Api.Versioning;
using ApexPost.Application.Tracking;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
// v2's response DTOs have v1's names; these aliases pick v2's over the v1 ones in ApexPost.Api.Contracts.
using ScanResponse = ApexPost.Api.Contracts.V2.ScanResponse;
using ScanUploadResponse = ApexPost.Api.Contracts.V2.ScanUploadResponse;
using ShipmentResponse = ApexPost.Api.Contracts.V2.ShipmentResponse;

namespace ApexPost.Api.Controllers.V2;

/// <summary>
/// Shipments, version 2: the same actions and the same service as version 1, with v2's response DTOs (nested
/// recipient, latest scan, enums by name). Each action binds its input, calls <see cref="ShipmentTrackingService"/>
/// once and chooses the success result. Every rule and every error is the service's: ApiExceptionHandler turns its
/// typed errors into 400, 404 and 409 responses, so no action catches anything.
/// </summary>
/// <remarks>
/// The OpenAPI description of each action: [EndpointSummary] is its one-line summary; the XML remarks, params and
/// responses below are its description, parameters and per-status descriptions. The route Name is the operationId,
/// and so the method name in a generated client: it must not change once published. An action's XML summary would
/// replace [EndpointSummary] in the document, so actions have none.
/// </remarks>
[ApiController]
[ApiVersion(ApiVersions.V2)]
[Route("api/v{version:apiVersion}/shipments")]
[Tags("Shipments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, MediaTypeNames.Application.ProblemJson)]
public sealed class ShipmentsController(ShipmentTrackingService tracking) : ControllerBase
{
    /// <remarks>
    /// Shipments are ordered by tracking number. Filter by status, by city (case does not matter) or both, and page
    /// through the results with page and pageSize.
    /// </remarks>
    /// <param name="query">The page to return and the optional filters.</param>
    /// <response code="200">One page of shipments, with the total count and number of pages.</response>
    /// <response code="400">page, pageSize, status or city is invalid; the errors name each one.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpGet(Name = "ListShipments")]
    [EndpointSummary("List shipments, one page at a time")]
    [ProducesResponseType<PagedResponse<ShipmentResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<PagedResponse<ShipmentResponse>>> ListShipments([FromQuery] ShipmentListQuery query, CancellationToken cancellationToken)
    {
        var page = await tracking.ListAsync(query.ToApplication(), cancellationToken);
        return Ok(PagedResponse.From(page, ShipmentResponse.From));
    }

    /// <param name="trackingNumber" example="APX-24000001">The shipment's tracking number: APX- and eight digits.</param>
    /// <response code="200">The shipment.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpGet("{trackingNumber:trackingNumber}", Name = "GetShipment")]
    [EndpointSummary("Get one shipment")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<ShipmentResponse>> GetShipment(string trackingNumber, CancellationToken cancellationToken) =>
        Ok(ShipmentResponse.From(await tracking.GetAsync(trackingNumber, cancellationToken)));

    /// <remarks>A new shipment starts in Created status. The tracking number is chosen by the caller and must be unused.</remarks>
    /// <param name="request">The tracking number, the recipient and the weight.</param>
    /// <response code="201">Created. The Location header is the shipment's URL.</response>
    /// <response code="400">A field is missing or invalid; the errors name each one.</response>
    /// <response code="409">The tracking number is already in use.</response>
    /// <response code="415">The body is not application/json.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpPost(Name = "CreateShipment")]
    [Consumes(MediaTypeNames.Application.Json)]
    [EndpointSummary("Create a shipment")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<ShipmentResponse>> CreateShipment(CreateShipmentRequest request, CancellationToken cancellationToken)
    {
        var created = await tracking.CreateAsync(request.ToApplication(), cancellationToken);
        return CreatedAtAction(nameof(GetShipment), new { trackingNumber = created.TrackingNumber }, ShipmentResponse.From(created));
    }

    /// <remarks>
    /// Replaces every editable detail: the recipient and the weight. The tracking number and the status do not
    /// change. A Delivered or Failed shipment can no longer be edited.
    /// </remarks>
    /// <param name="trackingNumber" example="APX-24000001">The shipment's tracking number: APX- and eight digits.</param>
    /// <param name="request">The new recipient and weight.</param>
    /// <response code="200">The shipment with its new details.</response>
    /// <response code="400">A field is missing or invalid; the errors name each one.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="409">The shipment is Delivered or Failed and can no longer be edited.</response>
    /// <response code="415">The body is not application/json.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpPut("{trackingNumber:trackingNumber}", Name = "UpdateShipment")]
    [Consumes(MediaTypeNames.Application.Json)]
    [EndpointSummary("Replace a shipment's details")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<ShipmentResponse>> UpdateShipment(string trackingNumber, UpdateShipmentRequest request, CancellationToken cancellationToken) =>
        Ok(ShipmentResponse.From(await tracking.ReplaceDetailsAsync(trackingNumber, request.ToApplication(), cancellationToken)));

    /// <remarks>
    /// Moves the shipment one step along Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed.
    /// Skipping a step, going back or leaving Delivered or Failed is refused.
    /// </remarks>
    /// <param name="trackingNumber" example="APX-24000001">The shipment's tracking number: APX- and eight digits.</param>
    /// <param name="request">The next status.</param>
    /// <response code="200">The shipment in its new status.</response>
    /// <response code="400">The status is missing or not a known status.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="409">The shipment cannot move from its current status to this one.</response>
    /// <response code="415">The body is not application/json.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpPatch("{trackingNumber:trackingNumber}/status", Name = "UpdateShipmentStatus")]
    [Consumes(MediaTypeNames.Application.Json)]
    [EndpointSummary("Change a shipment's status")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<ShipmentResponse>> UpdateShipmentStatus(string trackingNumber, UpdateStatusRequest request, CancellationToken cancellationToken) =>
        Ok(ShipmentResponse.From(await tracking.ChangeStatusAsync(trackingNumber, request.Status!.Value, cancellationToken)));

    /// <remarks>Only a shipment still in Created status can be deleted; after pickup it has a history to keep.</remarks>
    /// <param name="trackingNumber" example="APX-24000002">The shipment's tracking number: APX- and eight digits.</param>
    /// <response code="204">Deleted.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="409">The shipment is no longer in Created status.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [HttpDelete("{trackingNumber:trackingNumber}", Name = "DeleteShipment")]
    [EndpointSummary("Delete a shipment that is still Created")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<IActionResult> DeleteShipment(string trackingNumber, CancellationToken cancellationToken)
    {
        await tracking.DeleteAsync(trackingNumber, cancellationToken);
        return NoContent();
    }
}
