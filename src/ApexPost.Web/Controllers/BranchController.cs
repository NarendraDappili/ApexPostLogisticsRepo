using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class BranchController(IBranchRepository branches) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Dashboard(string? branchCode, CancellationToken cancellationToken)
    {
        branchCode = string.IsNullOrWhiteSpace(branchCode)
            ? User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value
            : branchCode;
        if (string.IsNullOrWhiteSpace(branchCode)) return View(new BranchDashboardViewModel(string.Empty, null, null, "Your account has no assigned branch."));
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
