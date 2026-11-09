using ApexPost.Api.Contracts;
using ApexPost.Application.Tracking;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>
/// Shipments. Each action binds its input, calls <see cref="ShipmentTrackingService"/> once and chooses the success
/// result. Every rule and every error is the service's: ApiExceptionHandler turns its typed errors into 400, 404 and
/// 409 responses, so no action catches anything.
/// </summary>
[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController(ShipmentTrackingService tracking) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ShipmentResponse>>> ListShipments([FromQuery] ShipmentListQuery query, CancellationToken cancellationToken)
    {
        var page = await tracking.ListAsync(query.ToApplication(), cancellationToken);
        return Ok(PagedResponse.From(page, ShipmentResponse.From));
    }

    [HttpGet("{trackingNumber:trackingNumber}")]
    public async Task<ActionResult<ShipmentResponse>> GetShipment(string trackingNumber, CancellationToken cancellationToken) =>
        Ok(ShipmentResponse.From(await tracking.GetAsync(trackingNumber, cancellationToken)));

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ShipmentResponse>> CreateShipment(CreateShipmentRequest request, CancellationToken cancellationToken)
    {
        var created = await tracking.CreateAsync(request.ToApplication(), cancellationToken);
        return CreatedAtAction(nameof(GetShipment), new { trackingNumber = created.TrackingNumber }, ShipmentResponse.From(created));
    }

    [HttpPut("{trackingNumber:trackingNumber}")]
    [Consumes("application/json")]
    public async Task<ActionResult<ShipmentResponse>> UpdateShipment(string trackingNumber, UpdateShipmentRequest request, CancellationToken cancellationToken) =>
        Ok(ShipmentResponse.From(await tracking.ReplaceDetailsAsync(trackingNumber, request.ToApplication(), cancellationToken)));

    [HttpPatch("{trackingNumber:trackingNumber}/status")]
    [Consumes("application/json")]
    public async Task<ActionResult<ShipmentResponse>> UpdateShipmentStatus(string trackingNumber, UpdateStatusRequest request, CancellationToken cancellationToken) =>
        Ok(ShipmentResponse.From(await tracking.ChangeStatusAsync(trackingNumber, request.Status!.Value, cancellationToken)));

    [HttpDelete("{trackingNumber:trackingNumber}")]
    public async Task<IActionResult> DeleteShipment(string trackingNumber, CancellationToken cancellationToken)
    {
        await tracking.DeleteAsync(trackingNumber, cancellationToken);
        return NoContent();
    }
}
