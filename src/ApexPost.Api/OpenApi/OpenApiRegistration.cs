using Microsoft.AspNetCore.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>
/// One OpenAPI document per API version, built by the built-in generator: /openapi/v1.json and /openapi/v2.json. A
/// document holds the API Explorer group of the same name, and Asp.Versioning names each version's group "v1", "v2"
/// (ApiVersioningRegistration), so each document describes exactly one version. Every document gets the same
/// transformers.
/// </summary>
internal static class OpenApiRegistration
{
    /// <summary>The documents, one per API version, by API Explorer group name.</summary>
    public static readonly IReadOnlyList<string> DocumentNames = ["v1", "v2"];

    public static IServiceCollection AddApexPostOpenApi(this IServiceCollection services)
    {
        foreach (var documentName in DocumentNames)
        {
            services.AddOpenApi(documentName, Configure);
        }
        return services;
    }

    private static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer<ApiDocumentInfoTransformer>();
        options.AddOperationTransformer<DeprecatedOperationTransformer>();
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.AddOperationTransformer<UnauthorizedResponseTransformer>();
        options.AddOperationTransformer<PartnerApiKeySecurityTransformer>();
        // Make the document describe the HTTP contract where it differs from the CLR types (see ContractTransformers).
        options.AddSchemaTransformer<EnumSchemaTransformer>();
        options.AddSchemaTransformer<NumberSchemaTransformer>();
        options.AddSchemaTransformer<RequiredNotNullSchemaTransformer>();
        options.AddOperationTransformer<CreatedLocationHeaderTransformer>();
    }
}
