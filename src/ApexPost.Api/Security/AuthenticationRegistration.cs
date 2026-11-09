using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>
/// JWT bearer authentication (SECURITY.md). The issuer, the audiences and the signing keys are read from
/// configuration, Authentication:Schemes:Bearer: in Development dotnet user-jwts writes the issuer and audiences to
/// appsettings.Development.json and the signing key to user-secrets; elsewhere they come from the deployment (or, with
/// a real identity provider, from its metadata). Every token is checked for its issuer, audience, lifetime and
/// signature.
/// Every endpoint needs an authenticated caller unless it is marked [AllowAnonymous], so a new endpoint is protected
/// without anyone remembering to protect it. A missing, invalid or expired token is a 401 ProblemDetails.
/// </summary>
internal static class AuthenticationRegistration
{
    public const string ConfigurationSection = "Authentication:Schemes:Bearer";

    public static IServiceCollection AddApexPostAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        RequireConfiguration(configuration.GetSection(ConfigurationSection));

        // AddJwtBearer() binds ValidIssuer, ValidAudiences and SigningKeys from Authentication:Schemes:Bearer itself.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var parameters = options.TokenValidationParameters;
                parameters.ValidateIssuer = true;
                parameters.ValidateAudience = true;
                parameters.ValidateLifetime = true;
                parameters.ValidateIssuerSigningKey = true;
                parameters.RequireExpirationTime = true;
                parameters.RequireSignedTokens = true;
                // The default allows tokens five minutes past their expiry; one minute covers ordinary clock drift.
                parameters.ClockSkew = TimeSpan.FromMinutes(1);
                options.Events = new JwtBearerEvents { OnChallenge = WriteUnauthorizedAsync };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    // Fail at start-up, like a missing connection string: an API with no way to validate tokens would answer 401 to
    // everyone, which is safe but baffling. With Authority set (a real identity provider), the issuer and the public
    // signing keys come from the provider's metadata instead.
    internal static void RequireConfiguration(IConfigurationSection bearer)
    {
        var usesAuthority = !string.IsNullOrWhiteSpace(bearer["Authority"]);
        var missing = new List<string>();
        if (!usesAuthority && string.IsNullOrWhiteSpace(bearer["ValidIssuer"])) missing.Add("ValidIssuer");
        if (!bearer.GetSection("ValidAudiences").GetChildren().Any(audience => !string.IsNullOrWhiteSpace(audience.Value))) missing.Add("ValidAudiences");
        if (!usesAuthority && !bearer.GetSection("SigningKeys").GetChildren().Any(key => !string.IsNullOrWhiteSpace(key["Value"]))) missing.Add("SigningKeys");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{ConfigurationSection} is missing {string.Join(", ", missing)}. In Development create the signing key and a token with "
                + "dotnet user-jwts create --project src/ApexPost.Api; elsewhere set Authority to the identity provider (see SECURITY.md).");
        }
    }

    // The default challenge sends the 401 and its WWW-Authenticate header without a body. This one sends the same
    // header (RFC 6750) and a ProblemDetails body that says what to fix, without naming the internal reason.
    private static async Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var (errorDescription, detail) = context.AuthenticateFailure switch
        {
            null => ((string?)null, "Send an access token in the Authorization header: Authorization: Bearer <token>."),
            SecurityTokenExpiredException => ("The token expired", "The access token has expired. Sign in again to get a new one."),
            _ => ("The token is not valid", "The access token is not valid for this API."),
        };

        var response = context.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = errorDescription is null
            ? JwtBearerDefaults.AuthenticationScheme
            : $"{JwtBearerDefaults.AuthenticationScheme} error=\"invalid_token\", error_description=\"{errorDescription}\"";

        var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication is required.",
                Detail = detail,
            },
        });
    }
}
