using ApexPost.Application.Reports;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "BranchManager,Auditor,Administrator", Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class ReportsController(BranchReportService reports, IConfiguration configuration) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? branchCode, DateOnly? day, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode)) return View(null);
        try
        {
            var branch = new BranchCode(branchCode);
            var reportDay = day ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var threshold = configuration.GetValue<int?>("ApexPost:Inventory:LowStockThreshold") ?? 10;
            var sales = await reports.GetDailySalesAsync(branch, reportDay, cancellationToken);
            var lowStock = await reports.GetLowStockAsync(branch, threshold, cancellationToken);
            var shipping = await reports.GetShippingVolumeAsync(branch, cancellationToken);
            var moneyOrders = await reports.GetMoneyOrderVerificationAsync(branch, cancellationToken);
            return View(new ReportsViewModel(branch.Value, reportDay, sales, lowStock, shipping, moneyOrders));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (ArgumentException) { return BadRequest(); }
    }
}
