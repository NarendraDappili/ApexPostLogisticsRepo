using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexPost.Web.Controllers;

[ApiController]
[Route("integration")]
[RequireIntegrationApiKey]
[RequireIpAllowlist]
[EnableCors("PartnerTrackingWidget")]
[EnableRateLimiting("partner-integration")]
public sealed class IntegrationController(IShipmentRepository shipments) : ControllerBase
{
    [HttpGet("parcel-status")]
    public async Task<IActionResult> ParcelStatus(string shipmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shipmentId)) return BadRequest();
        try
        {
            var shipment = await shipments.FindByIdAsync(new ShipmentId(shipmentId), cancellationToken).ConfigureAwait(false);
            return shipment is null
                ? NotFound()
                : Ok(new { shipmentId = shipment.Id.Value, originBranch = shipment.OriginBranchCode.Value, destinationZone = shipment.DestinationZoneCode.Value, serviceLevel = shipment.ServiceLevel.ToString(), shipmentType = shipment.ShipmentType.ToString() });
        }
        catch (ArgumentException) { return BadRequest(); }
    }
}
