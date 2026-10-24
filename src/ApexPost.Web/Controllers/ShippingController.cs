using ApexPost.Application.Shipping;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "CounterClerk")]
public sealed class ShippingController(ShippingWorkflowService shipping) : Controller
{
    [HttpGet]
    public IActionResult Create() => View(new ShipmentFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShipmentFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        if (!string.Equals(User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value, input.OriginBranchCode, StringComparison.OrdinalIgnoreCase)) return Forbid();
        try
        {
            var result = await shipping.CreateAsync(new ShipmentCreationRequest(input.ShipmentId, input.OriginBranchCode, input.DestinationZoneCode, input.ShipmentType, input.ServiceLevel, input.WeightKilograms, input.LengthCentimetres, input.WidthCentimetres, input.HeightCentimetres), cancellationToken);
            TempData["Charge"] = result.Price.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return RedirectToAction(nameof(Label), new { id = result.Shipment.Id.Value });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { ModelState.AddModelError(string.Empty, exception.Message); return View(input); }
    }

    [HttpGet]
    public async Task<IActionResult> Label(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var shipment = await shipping.GetAsync(new ShipmentId(id), cancellationToken);
            decimal? charge = decimal.TryParse(TempData["Charge"] as string, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            return View(new ShippingLabelViewModel(shipment, charge));
        }
        catch (ShipmentNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
    }
}
