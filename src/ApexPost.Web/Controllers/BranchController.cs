using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

public sealed class BranchController(IBranchRepository branches) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Dashboard(string? branchCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode)) return View(new BranchDashboardViewModel(string.Empty, null, null, "Enter a branch code to view its dashboard."));
        try
        {
            var branch = await branches.FindByCodeAsync(new BranchCode(branchCode), cancellationToken);
            return branch is null
                ? NotFound()
                : View(new BranchDashboardViewModel(branch.Code.Value, branch.Name, branch.IsOpen));
        }
        catch (ArgumentException) { return BadRequest(); }
    }
}
