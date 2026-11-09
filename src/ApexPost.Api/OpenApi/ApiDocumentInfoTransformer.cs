using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>
/// Gives the generated OpenAPI document the information a client developer reads first: the API's name, what it is
/// for, its version and who to contact. The generator writes the paths and schemas; this transformer writes only the
/// <c>info</c> section. The contact details are fictional (the reserved <c>.example</c> domain).
/// </summary>
internal sealed class ApiDocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public const string Title = "ApexPost Logistics API";
    public const string Version = "1.0";
    public const string ContactName = "ApexPost Platform Team";
    public const string ContactEmail = "platform-team@apexpost.example";
    public const string ContactUrl = "https://developer.apexpost.example/support";

    public const string Description =
        "Tracks ApexPost shipments from creation to delivery. Partners and the customer portal create, read, update "
        + "and delete tracked shipments and change their status; courier devices upload the scans they recorded "
        + "offline, and re-uploading a scan never records it twice. Every error is an RFC 9457 ProblemDetails "
        + "(application/problem+json) with a traceId to quote when reporting a problem.";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = Title,
            Version = Version,
            Description = Description,
            Contact = new OpenApiContact
            {
                Name = ContactName,
                Email = ContactEmail,
                Url = new Uri(ContactUrl),
            },
        };
        return Task.CompletedTask;
    }
}
