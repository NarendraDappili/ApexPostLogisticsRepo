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

    public async Task<ClaimsPrincipal?> AuthenticateAsync(string staffId, string password, CancellationToken cancellationToken = default) =>
        (await AuthenticateWithOutcomeAsync(staffId, password, cancellationToken).ConfigureAwait(false)).Principal;

    /// <summary>
    /// Authenticates and also says why a sign-in failed, for the security log only. The person signing in is
    /// always told the same "Sign-in failed", whatever the reason.
    /// </summary>
    public async Task<StaffSignInOutcome> AuthenticateWithOutcomeAsync(string staffId, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(staffId) || string.IsNullOrEmpty(password)) return StaffSignInOutcome.Failed(null, "MissingCredentials");

        StaffId id;
        try { id = new StaffId(staffId); }
        catch (ArgumentException) { return StaffSignInOutcome.Failed(null, "MalformedStaffId"); }
        var member = await staff.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        var passwordMatches = passwordHasher.Verify(password, member?.PasswordHash ?? _timingEqualizerHash);
        // What was typed is kept only when it is a real staff ID: an unknown "ID" may be a password typed into the wrong field.
        if (member is null) return StaffSignInOutcome.Failed(null, "UnknownStaff");
        if (member.PasswordHash is null) return StaffSignInOutcome.Failed(id.Value, "NoPasswordSet");
        if (!passwordMatches) return StaffSignInOutcome.Failed(id.Value, "WrongPassword");
        if (!member.IsActive) return StaffSignInOutcome.Failed(id.Value, "Inactive");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, member.Id.Value),
            new Claim(ClaimTypes.Name, member.Name),
            new Claim(ClaimTypes.Role, member.Role.ToString()),
            new Claim(ApexPostClaimTypes.StaffId, member.Id.Value),
            new Claim(ApexPostClaimTypes.BranchCode, member.AssignedBranchCode.Value)
        };
        return new StaffSignInOutcome(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), id.Value, null);
    }
}

/// <param name="Principal">The signed-in staff member, or null when the sign-in failed.</param>
/// <param name="StaffId">The staff ID that was typed, only when it belongs to a real staff member.</param>
/// <param name="FailureReason">Why the sign-in failed, for the security log only.</param>
public sealed record StaffSignInOutcome(ClaimsPrincipal? Principal, string? StaffId, string? FailureReason)
{
    public static StaffSignInOutcome Failed(string? staffId, string reason) => new(null, staffId, reason);
}
