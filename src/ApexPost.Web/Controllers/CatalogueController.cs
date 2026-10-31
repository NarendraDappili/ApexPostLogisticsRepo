using ApexPost.Application.Catalogue;
using ApexPost.Application.Inventory;
using ApexPost.Application.Queries;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Filters;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class CatalogueController(ICatalogueQueries catalogue) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? branchCode, CancellationToken cancellationToken)
    {
        branchCode = string.IsNullOrWhiteSpace(branchCode)
            ? User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value
            : branchCode;
        var categories = User.IsInRole("Administrator") ? await catalogue.GetCategoriesAsync(cancellationToken) : null;
        if (string.IsNullOrWhiteSpace(branchCode))
            return View(new CatalogueViewModel(string.Empty, ToViewModels(await catalogue.GetCatalogueAsync(null, cancellationToken)), "Your account has no assigned branch; stock is unavailable.", categories));
        try
        {
            var items = await catalogue.GetCatalogueAsync(new BranchCode(branchCode), cancellationToken);
            return View(new CatalogueViewModel(branchCode, ToViewModels(items), Categories: categories));
        }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpGet]
    public async Task<IActionResult> StockJson(string branchCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode)) return BadRequest();
        try
        {
            var stock = await catalogue.GetStockLevelsAsync(new BranchCode(branchCode), cancellationToken);
            return Json(stock.Select(item => new { productId = item.ProductId, quantity = item.Quantity }));
        }
        catch (ArgumentException) { return BadRequest(); }
    }

    /// <summary>Set-based price revision for one category; administrators only, like the other catalogue-wide changes.</summary>
    /// <remarks>The form posts here and redirects back, so an invalid model is answered with 400 Problem Details.</remarks>
    [Authorize(Roles = "Administrator"), RequireIpAllowlist, HttpPost, ValidateAntiForgeryToken, InvalidModelResponse]
    public async Task<IActionResult> RevisePrices(PriceRevisionViewModel input, [FromServices] PriceRevisionService priceRevision, CancellationToken cancellationToken)
    {
        try
        {
            var repriced = await priceRevision.ReviseAsync(new PriceRevisionRequest(input.CategoryCode, input.Percentage), cancellationToken);
            TempData["Notice"] = $"{repriced} product(s) in {input.CategoryCode.Trim().ToUpperInvariant()} were repriced by {input.Percentage.ToString("+0.##;-0.##", System.Globalization.CultureInfo.InvariantCulture)}%.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (ArgumentException exception) { TempData["Error"] = exception.Message; }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>A Branch Manager corrects stock at their own branch (usp_AdjustStock writes the change and its history).</summary>
    [Authorize(Policy = AuthorizationPolicies.BranchManagerOnly), HttpPost, ValidateAntiForgeryToken, InvalidModelResponse]
    public async Task<IActionResult> AdjustStock(StockAdjustmentViewModel input, [FromServices] StockAdjustmentService stockAdjustments, CancellationToken cancellationToken)
    {
        var managerId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value;
        var branchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        if (string.IsNullOrWhiteSpace(managerId) || string.IsNullOrWhiteSpace(branchCode)) return Forbid();
        try
        {
            var level = await stockAdjustments.AdjustAsync(new StockAdjustmentRequest(branchCode, input.ProductId, input.Delta, input.Reason), new StaffId(managerId), cancellationToken);
            TempData["Notice"] = $"Stock of {level.ProductId.Value} at {level.BranchCode.Value} is now {level.Quantity} ({input.Delta.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture)}).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            this.UseConflictStatus(exception);
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(Index), new { branchCode });
    }

    private static CatalogueItemViewModel[] ToViewModels(IReadOnlyList<CatalogueItem> items) =>
        items.Select(item => new CatalogueItemViewModel(item.ProductId, item.Sku, item.Name, item.CategoryCode, item.UnitPrice, item.StockQuantity)).ToArray();
}
