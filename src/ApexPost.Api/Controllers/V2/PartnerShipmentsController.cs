using System.Net.Mime;
using ApexPost.Api.Contracts.V2;
using ApexPost.Api.Security;
using ApexPost.Api.Versioning;
using ApexPost.Application.Tracking;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers.V2;

/// <summary>
/// The read-only route for partner companies, version 2 only. Partners send an API key in X-Api-Key instead of a
/// bearer token: [AllowAnonymous] lifts the bearer requirement and [RequirePartnerApiKey] puts the key check in its
/// place (PartnerApiKeyFilter). Documented the same way as <see cref="ShipmentsController"/>.
/// </summary>
[ApiController]
[ApiVersion(ApiVersions.V2)]
[Route("api/v{version:apiVersion}/partner/shipments")]
[Tags("Partner")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, MediaTypeNames.Application.ProblemJson)]
public sealed class PartnerShipmentsController(ShipmentTrackingService tracking) : ControllerBase
{
    /// <remarks>
    /// For partner companies: the shipment's status, delivery city and latest scan. The recipient's name, phone and
    /// address are not included. Authenticate with the partner API key in the X-Api-Key header; a bearer token is not
    /// accepted here.
    /// </remarks>
    /// <param name="trackingNumber" example="APX-24000001">The shipment's tracking number: APX- and eight digits.</param>
    /// <response code="200">The shipment, as a partner sees it.</response>
    /// <response code="401">No valid partner API key in the X-Api-Key header.</response>
    /// <response code="404">No shipment has this tracking number, or it is not in the APX-######## form.</response>
    /// <response code="500">An unexpected server error. Quote the traceId when reporting it.</response>
    [AllowAnonymous]
    [RequirePartnerApiKey]
    [HttpGet("{trackingNumber:trackingNumber}", Name = "GetPartnerShipment")]
    [EndpointSummary("Track a shipment (partners, with an API key)")]
    [ProducesResponseType<PartnerShipmentResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<PartnerShipmentResponse>> GetPartnerShipment(string trackingNumber, CancellationToken cancellationToken) =>
        Ok(PartnerShipmentResponse.From(await tracking.GetAsync(trackingNumber, cancellationToken)));
}
