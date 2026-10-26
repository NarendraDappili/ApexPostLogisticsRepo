using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class CatalogueController(IProductRepository products, IInventoryRepository inventory) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? branchCode, CancellationToken cancellationToken)
    {
        var catalogue = await products.GetAllAsync(cancellationToken);
        branchCode = string.IsNullOrWhiteSpace(branchCode)
            ? User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value
            : branchCode;
        if (string.IsNullOrWhiteSpace(branchCode))
            return View(new CatalogueViewModel(string.Empty, catalogue.Select(product => new CatalogueItemViewModel(product.Id.Value, product.Name, product.UnitPrice.Amount, null)).ToArray(), "Your account has no assigned branch; stock is unavailable."));
        try
        {
            var stock = await inventory.FindByBranchAsync(new BranchCode(branchCode), cancellationToken);
            var quantities = stock.ToDictionary(item => item.ProductId.Value, item => item.Quantity, StringComparer.OrdinalIgnoreCase);
            return View(new CatalogueViewModel(branchCode, catalogue.Select(product => new CatalogueItemViewModel(product.Id.Value, product.Name, product.UnitPrice.Amount, quantities.GetValueOrDefault(product.Id.Value))).ToArray()));
        }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpGet]
    public async Task<IActionResult> StockJson(string branchCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode)) return BadRequest();
        try
        {
            var stock = await inventory.FindByBranchAsync(new BranchCode(branchCode), cancellationToken);
            return Json(stock.Select(item => new { productId = item.ProductId.Value, quantity = item.Quantity }));
        }
        catch (ArgumentException) { return BadRequest(); }
    }
}
