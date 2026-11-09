using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>
/// POST /dev/token, in Development only: issues a short-lived token for a courier or an admin, signed with the same key
/// and for the same issuer and audience that dotnet user-jwts configured. It lets the courier app's Login screen and the
/// .http file get a token without the command line. It is a stand-in for an identity provider: anyone who can reach it
/// can get any role, which is why it is never mapped outside Development (SECURITY.md). It is not part of the API's
/// contract, so it is in no OpenAPI document.
/// </summary>
internal static class DevelopmentTokenEndpoint
{
    public const string Path = "/dev/token";

    /// <summary>As short as a real identity provider's access tokens: a lost token is useless after an hour.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static IEndpointConventionBuilder MapDevelopmentTokens(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Path, Issue).AllowAnonymous().ExcludeFromDescription();

    private static IResult Issue(DevelopmentTokenRequest request, IOptionsMonitor<JwtBearerOptions> bearer, TimeProvider clock)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var parameters = bearer.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var key = parameters.IssuerSigningKeys?.FirstOrDefault() ?? parameters.IssuerSigningKey
            ?? throw new InvalidOperationException("No signing key is configured; run dotnet user-jwts create --project src/ApexPost.Api.");
        var now = clock.GetUtcNow();
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = request.Name!,
            [JwtRegisteredClaimNames.Name] = request.Name!,
            ["role"] = request.Roles!.Distinct(StringComparer.Ordinal).ToArray(),
        };
        if (!string.IsNullOrWhiteSpace(request.CourierId)) claims[ApiRoles.CourierIdClaim] = request.CourierId;

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = parameters.ValidIssuer ?? parameters.ValidIssuers?.First(),
            Audience = parameters.ValidAudience ?? parameters.ValidAudiences?.First(),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + Lifetime).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
        return TypedResults.Ok(new DevelopmentTokenResponse(token, "Bearer", now + Lifetime));
    }

    private static Dictionary<string, string[]> Validate(DevelopmentTokenRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["name is required."];
        if (request.Roles is not { Count: > 0 } roles || roles.Any(role => !ApiRoles.All.Contains(role)))
        {
            errors["roles"] = [$"roles must be one or more of: {string.Join(", ", ApiRoles.All)}."];
        }
        else if (roles.Contains(ApiRoles.Courier) && string.IsNullOrWhiteSpace(request.CourierId))
        {
            errors["courierId"] = ["A Courier token needs the courierId its scans are uploaded under."];
        }
        if (request.CourierId is { Length: > 32 }) errors["courierId"] = ["courierId can have at most 32 characters."];
        return errors;
    }
}

/// <summary>Who the development token is for.</summary>
/// <param name="Name">The user's name, for example "Kofi Boateng".</param>
/// <param name="Roles">Courier, Admin or both.</param>
/// <param name="CourierId">The courier's ID, required for the Courier role, for example "CR-1042".</param>
internal sealed record DevelopmentTokenRequest(string? Name, IReadOnlyList<string>? Roles, string? CourierId);

/// <summary>A development access token.</summary>
/// <param name="AccessToken">The JWT to send as Authorization: Bearer &lt;token&gt;.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresAtUtc">When the token stops being accepted.</param>
internal sealed record DevelopmentTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);
