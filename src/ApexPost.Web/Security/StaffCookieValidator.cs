using System.Security.Claims;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApexPost.Web.Security;

/// <summary>Rejects cookies for removed, inactive, or changed staff records.</summary>
public static class StaffCookieValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var rawStaffId = principal?.FindFirstValue(ApexPostClaimTypes.StaffId);
        if (principal is null || string.IsNullOrWhiteSpace(rawStaffId)) { context.RejectPrincipal(); return; }
        try
        {
            var staff = await context.HttpContext.RequestServices.GetRequiredService<IStaffMemberRepository>()
                .FindByIdAsync(new StaffId(rawStaffId), context.HttpContext.RequestAborted).ConfigureAwait(false);
            if (staff is null || !staff.IsActive ||
                !string.Equals(staff.Role.ToString(), principal.FindFirstValue(ClaimTypes.Role), StringComparison.Ordinal) ||
                !string.Equals(staff.AssignedBranchCode.Value, principal.FindFirstValue(ApexPostClaimTypes.BranchCode), StringComparison.OrdinalIgnoreCase))
            {
                context.RejectPrincipal();
            }
        }
        catch (ArgumentException) { context.RejectPrincipal(); }
    }
}
