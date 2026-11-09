using System.Net.Http.Headers;
using System.Security.Cryptography;
using ApexPost.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Tests.Hosting;

/// <summary>
/// Bearer tokens for the test hosts. Each test run makes up its own signing key, issuer and audience and gives them
/// to the API through configuration, the same section dotnet user-jwts fills in Development, so a developer's real
/// key is never used by the tests.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "apexpost-tests";
    public const string Audience = "apexpost-api-tests";

    /// <summary>The courier the default test token belongs to.</summary>
    public const string CourierId = "CR-TEST";

    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>This run's key, for a test that builds an unusual token itself.</summary>
    public static SigningCredentials Credentials => new(new SymmetricSecurityKey(SigningKey), SecurityAlgorithms.HmacSha256);

    /// <summary>Points the API's token validation at this run's issuer, audience and key.</summary>
    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", Issuer);
        builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", Audience);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", Issuer);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(SigningKey));
    }

    /// <summary>The header every test client sends unless a test says otherwise: both roles, courier CR-TEST.</summary>
    public static AuthenticationHeaderValue DefaultHeader() => new("Bearer", Create([ApiRoles.Courier, ApiRoles.Admin], CourierId));

    /// <summary>A token that the test host accepts, unless one of the optional values is changed to make it invalid.</summary>
    public static string Create(
        IReadOnlyList<string> roles,
        string? courierId = CourierId,
        DateTime? expires = null,
        string issuer = Issuer,
        string audience = Audience,
        byte[]? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = "test-user", ["role"] = roles.ToArray() };
        if (courierId is not null) claims[ApiRoles.CourierIdClaim] = courierId;
        var expiry = expires ?? now.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expiry < now ? expiry.AddMinutes(-10) : now,
            NotBefore = expiry < now ? expiry.AddMinutes(-10) : now,
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? SigningKey), SecurityAlgorithms.HmacSha256),
        });
    }
}
