using ApexPost.Application.Reports;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "BranchManager,Auditor,Administrator", Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class ReportsController(IBranchReportPageQueries reports, IConfiguration configuration, TimeProvider timeProvider) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? branchCode, DateOnly? day, CancellationToken cancellationToken)
    {
        branchCode = string.IsNullOrWhiteSpace(branchCode)
            ? User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value
            : branchCode;
        if (string.IsNullOrWhiteSpace(branchCode)) return View(null);
        try
        {
            var branch = new BranchCode(branchCode);
            var reportDay = day ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var threshold = configuration.GetValue<int?>("ApexPost:Inventory:LowStockThreshold") ?? 10;
            // The four reports are independent reads, run in parallel on separate contexts (ADR 0021).
            var page = await reports.GetAsync(branch, reportDay, threshold, cancellationToken);
            return View(new ReportsViewModel(branch.Value, reportDay, page.DailySales, page.LowStock, page.ShippingVolume, page.MoneyOrders));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (ArgumentException) { return BadRequest(); }
    }
}
