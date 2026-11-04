using System.Security.Claims;
using ApexPost.Application.Auditing;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApexPost.Web.Security;

/// <summary>
/// Rejects cookies for removed, inactive, or changed staff records. Each rejection is recorded as a SessionRejected
/// security event with the reason; the request then continues as an anonymous one (401: no longer signed in).
/// </summary>
public static class StaffCookieValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var rawStaffId = principal?.FindFirstValue(ApexPostClaimTypes.StaffId);
        if (principal is null || string.IsNullOrWhiteSpace(rawStaffId)) { await RejectAsync(context, principal, "MissingStaffId").ConfigureAwait(false); return; }
        try
        {
            var staff = await context.HttpContext.RequestServices.GetRequiredService<IStaffMemberRepository>()
                .FindByIdAsync(new StaffId(rawStaffId), context.HttpContext.RequestAborted).ConfigureAwait(false);
            var reason = staff is null ? "StaffNotFound"
                : !staff.IsActive ? "Inactive"
                : !string.Equals(staff.Role.ToString(), principal.FindFirstValue(ClaimTypes.Role), StringComparison.Ordinal) ? "RoleChanged"
                : !string.Equals(staff.AssignedBranchCode.Value, principal.FindFirstValue(ApexPostClaimTypes.BranchCode), StringComparison.OrdinalIgnoreCase) ? "BranchChanged"
                : null;
            if (reason is not null) await RejectAsync(context, principal, reason).ConfigureAwait(false);
        }
        catch (ArgumentException) { await RejectAsync(context, principal, "MalformedStaffId").ConfigureAwait(false); }
    }

    private static Task RejectAsync(CookieValidatePrincipalContext context, ClaimsPrincipal? principal, string reason)
    {
        context.RejectPrincipal();
        return SecurityEventRecorder.RecordAsync(context.HttpContext, SecurityEventType.SessionRejected, StatusCodes.Status401Unauthorized, reason, principal ?? new ClaimsPrincipal());
    }
}
