using System.Security.Claims;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApexPost.Web.Security;

/// <summary>Authenticates staff against the PBKDF2 password hash stored on their staff record.</summary>
public sealed class LocalStaffAuthenticator(IStaffMemberRepository staff, IPasswordHasher passwordHasher)
{
    // Verified when the staff ID is unknown or has no password, so every failed sign-in costs the same PBKDF2 work
    // and response timing does not reveal which staff IDs exist.
    private readonly string _timingEqualizerHash = passwordHasher.Hash(Guid.NewGuid().ToString("N"));

    public async Task<ClaimsPrincipal?> AuthenticateAsync(string staffId, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(staffId) || string.IsNullOrEmpty(password)) return null;

        StaffId id;
        try { id = new StaffId(staffId); }
        catch (ArgumentException) { return null; }
        var member = await staff.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        var passwordMatches = passwordHasher.Verify(password, member?.PasswordHash ?? _timingEqualizerHash);
        if (member is null || member.PasswordHash is null || !passwordMatches || !member.IsActive) return null;

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
}
