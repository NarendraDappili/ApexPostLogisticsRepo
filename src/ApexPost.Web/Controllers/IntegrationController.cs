using ApexPost.Application.Shipping;
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
public sealed class IntegrationController(ParcelStatusService parcelStatus) : ControllerBase
{
    [HttpGet("parcel-status")]
    public async Task<IActionResult> ParcelStatus(string shipmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shipmentId)) return BadRequest();
        try
        {
            var status = await parcelStatus.GetAsync(new ShipmentId(shipmentId), cancellationToken).ConfigureAwait(false);
            return status is null
                ? NotFound()
                : Ok(new
                {
                    shipmentId = status.Shipment.Id.Value,
                    originBranch = status.Shipment.OriginBranchCode.Value,
                    destinationZone = status.Shipment.DestinationZoneCode.Value,
                    serviceLevel = status.Shipment.ServiceLevel.ToString(),
                    shipmentType = status.Shipment.ShipmentType.ToString(),
                    courierStatus = status.CourierStatus,
                    courierLookupTimedOut = status.CourierLookupTimedOut
                });
        }
        catch (ArgumentException) { return BadRequest(); }
    }
}
