using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>
/// Marks every operation of a deprecated API version as deprecated, so Swagger UI strikes it through and a client
/// generator flags the method it generates. The version's deprecation comes from its [ApiVersion(Deprecated = true)].
/// </summary>
internal sealed class DeprecatedOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.IsDeprecated)
        {
            operation.Deprecated = true;
        }
        return Task.CompletedTask;
    }
}
