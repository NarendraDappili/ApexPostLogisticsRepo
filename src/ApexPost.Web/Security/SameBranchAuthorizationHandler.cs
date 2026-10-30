using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ApexPost.Web.Security;

public sealed class SameBranchRequirement : IAuthorizationRequirement;

/// <summary>
/// Matches an optional route/query branch code to the authenticated staff branch. Administrators and Auditors
/// work across branches (branch management and the per-branch auditor view), so they may view any branch.
/// </summary>
public sealed class SameBranchAuthorizationHandler : AuthorizationHandler<SameBranchRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameBranchRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var requestedBranch = httpContext?.Request.RouteValues["branchCode"]?.ToString()
            ?? httpContext?.Request.Query["branchCode"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestedBranch)
            || BranchAccess.CanViewAllBranches(context.User)
            || string.Equals(requestedBranch, context.User.FindFirstValue(ApexPostClaimTypes.BranchCode), StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class BranchAccess
{
    public static bool CanViewAllBranches(ClaimsPrincipal user) => user.IsInRole("Administrator") || user.IsInRole("Auditor");
}
