using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApexPost.Web.Security;

/// <summary>Authenticates local demo accounts configured outside source control.</summary>
public sealed class LocalStaffAuthenticator(IConfiguration configuration, IStaffMemberRepository staff)
{
    public async Task<ClaimsPrincipal?> AuthenticateAsync(string staffId, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(staffId) || string.IsNullOrEmpty(password)) return null;
        var configuredPassword = configuration[$"ApexPost:Authentication:DemoAccounts:{staffId.Trim()}"];
        if (string.IsNullOrEmpty(configuredPassword) || !FixedTimeEquals(configuredPassword, password)) return null;

        StaffId id;
        try { id = new StaffId(staffId); }
        catch (ArgumentException) { return null; }
        var member = await staff.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (member is null || !member.IsActive) return null;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, member.Id.Value),
            new Claim(ClaimTypes.Name, member.Name),
            new Claim(ClaimTypes.Role, member.Role.ToString()),
            new Claim(ApexPostClaimTypes.StaffId, member.Id.Value),
            new Claim(ApexPostClaimTypes.BranchCode, member.AssignedBranchCode.Value)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
