using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ApexPost.Web.Security;

public sealed class SameBranchRequirement : IAuthorizationRequirement;

/// <summary>Matches an optional route/query branch code to the authenticated staff branch.</summary>
public sealed class SameBranchAuthorizationHandler : AuthorizationHandler<SameBranchRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameBranchRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var requestedBranch = httpContext?.Request.RouteValues["branchCode"]?.ToString()
            ?? httpContext?.Request.Query["branchCode"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestedBranch) || string.Equals(requestedBranch, context.User.FindFirstValue(ApexPostClaimTypes.BranchCode), StringComparison.OrdinalIgnoreCase))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
