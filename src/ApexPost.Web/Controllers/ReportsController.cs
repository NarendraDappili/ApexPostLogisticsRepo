using ApexPost.Application.Inventory;
using ApexPost.Application.Reports;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "BranchManager,Auditor,Administrator", Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class ReportsController(IBranchReportPageQueries reports, InventoryServiceOptions inventoryOptions, TimeProvider timeProvider) : Controller
{
    /// <summary>
    /// The reports for a sales period from <paramref name="from"/> to <paramref name="to"/> (UTC days, both included).
    /// Without dates it shows today. <paramref name="day"/> is the earlier single-day form of the same request.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? branchCode, DateOnly? from, DateOnly? to, DateOnly? day, CancellationToken cancellationToken)
    {
        branchCode = string.IsNullOrWhiteSpace(branchCode)
            ? User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value
            : branchCode;
        if (string.IsNullOrWhiteSpace(branchCode)) return View(null);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var end = to ?? day ?? (from is { } start && start > today ? start : today);
        var begin = from ?? day ?? end;
        BranchCode branch;
        try { branch = new BranchCode(branchCode); }
        catch (ArgumentException) { return BadRequest(); }

        if (BranchSalesPeriodReport.FindProblem(begin, end) is { } problem)
        {
            // A period the user can correct: show the form again with the reason, as a 400.
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new ReportsViewModel(branch.Value, begin, end, today, null, problem));
        }

        try
        {
            // The four reports are independent reads, run in parallel on separate contexts (ADR 0021).
            var page = await reports.GetAsync(branch, begin, end, inventoryOptions.LowStockThreshold, cancellationToken);
            return View(new ReportsViewModel(branch.Value, begin, end, today, page));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (ArgumentException) { return BadRequest(); }
    }
}
