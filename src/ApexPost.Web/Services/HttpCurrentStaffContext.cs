using ApexPost.Application.Abstractions;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Security;

namespace ApexPost.Web.Services;

/// <summary>
/// The signed-in staff member of the current request, read from the authentication cookie's claims.
/// Outside a request (start-up seeding, background work) there is no staff member and both values are null.
/// </summary>
public sealed class HttpCurrentStaffContext(IHttpContextAccessor httpContextAccessor) : ICurrentStaffContext
{
    public StaffId? StaffId => Claim(ApexPostClaimTypes.StaffId) is { } value ? new StaffId(value) : null;

    public BranchCode? BranchCode => Claim(ApexPostClaimTypes.BranchCode) is { } value ? new BranchCode(value) : null;

    private string? Claim(string type) =>
        httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value is { Length: > 0 } value ? value : null;
}
