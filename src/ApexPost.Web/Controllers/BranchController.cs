using ApexPost.Application.Inventory;
using ApexPost.Application.Queries;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class BranchController(IBranchQueries branches, InventoryServiceOptions inventoryOptions, TimeProvider timeProvider) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Dashboard(string? branchCode, CancellationToken cancellationToken)
    {
        branchCode = string.IsNullOrWhiteSpace(branchCode)
            ? User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value
            : branchCode;
        if (string.IsNullOrWhiteSpace(branchCode)) return View(new BranchDashboardViewModel(string.Empty, null, "Your account has no assigned branch."));
        try
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var dashboard = await branches.GetDashboardAsync(new BranchCode(branchCode), today, inventoryOptions.LowStockThreshold, cancellationToken);
            return dashboard is null ? NotFound() : View(new BranchDashboardViewModel(dashboard.BranchCode, dashboard));
        }
        catch (ArgumentException) { return BadRequest(); }
    }
}
