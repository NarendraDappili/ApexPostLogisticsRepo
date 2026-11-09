using ApexPost.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>
/// Declares the Bearer scheme (a JWT in the Authorization header) and requires it for the whole document, so Swagger
/// UI shows an Authorize button and a generated client knows to send a token. An operation with its own security
/// (the partner route's API key) overrides the document's requirement.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "A JWT access token with the Courier or Admin role, sent as Authorization: Bearer <token>. "
                + "In Development, get one from dotnet user-jwts or POST /dev/token (SECURITY.md).",
        };
        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] }];
        return Task.CompletedTask;
    }
}

/// <summary>
/// The partner route takes an API key in X-Api-Key instead of a bearer token. This declares the key's scheme in the
/// document that has the route (v2) and gives the route its own security requirement, which overrides the document's
/// Bearer requirement, so Swagger UI asks for the key there and a generated client sends the header.
/// </summary>
internal sealed class PartnerApiKeySecurityTransformer : IOpenApiOperationTransformer
{
    public const string SchemeName = "PartnerApiKey";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<RequirePartnerApiKeyAttribute>().Any()) return Task.CompletedTask;

        var document = context.Document!;
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = PartnerApiKeyFilter.HeaderName,
            Description = "The API key ApexPost issued to your company, sent as X-Api-Key: <key>. Partner routes only (SECURITY.md).",
        };
        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] }];
        return Task.CompletedTask;
    }
}

/// <summary>
/// Describes the 401 that every protected operation declares. The response is the same for all of them, so it is
/// described once here rather than in every action's XML comments.
/// </summary>
internal sealed class UnauthorizedResponseTransformer : IOpenApiOperationTransformer
{
    public const string Description = "No valid access token: it is missing, expired, or not issued for this API. The WWW-Authenticate header says which.";

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var anonymous = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
        if (!anonymous && operation.Responses?.TryGetValue("401", out var unauthorized) == true && unauthorized is OpenApiResponse response)
        {
            response.Description = Description;
        }
        return Task.CompletedTask;
    }
}
