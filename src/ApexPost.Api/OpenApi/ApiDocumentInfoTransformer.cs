using System.Globalization;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>
/// Gives each version's OpenAPI document the information a client developer reads first: the API's name, what it is
/// for, its version and who to contact. The generator writes the paths and schemas; this transformer writes only the
/// <c>info</c> section. The contact details are fictional (the reserved <c>.example</c> domain).
/// A deprecated version's description starts with a notice: since when it is deprecated, when it will stop answering
/// and which version to use instead. The dates and the link come from the versioning policies, the same ones that
/// write v1's Deprecation and Sunset headers, so the document and the headers cannot disagree.
/// </summary>
internal sealed class ApiDocumentInfoTransformer(IApiVersionDescriptionProvider versions) : IOpenApiDocumentTransformer
{
    public const string Title = "ApexPost Logistics API";
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
        var version = versions.ApiVersionDescriptions.Single(description => description.GroupName == context.DocumentName);
        document.Info = new OpenApiInfo
        {
            Title = Title,
            Version = version.ApiVersion.ToString(),
            Description = version.IsDeprecated ? $"{DeprecationNotice(version)}\n\n{Description}" : Description,
            Contact = new OpenApiContact
            {
                Name = ContactName,
                Email = ContactEmail,
                Url = new Uri(ContactUrl),
            },
        };
        return Task.CompletedTask;
    }

    private string DeprecationNotice(ApiVersionDescription version)
    {
        var current = versions.ApiVersionDescriptions.Where(description => !description.IsDeprecated).MaxBy(description => description.ApiVersion);
        var notice = $"**Deprecated{On(" since", version.DeprecationPolicy?.Date)}.**";
        if (version.SunsetPolicy?.Date is { } sunset)
        {
            notice += $" This version stops answering after{On("", sunset)}.";
        }
        if (current is not null)
        {
            notice += $" Use {current.GroupName} instead.";
        }
        var link = version.SunsetPolicy?.Links.FirstOrDefault() ?? version.DeprecationPolicy?.Links.FirstOrDefault();
        return link is null ? notice : $"{notice} What changed and how to move: {link.LinkTarget}";
    }

    private static string On(string prefix, DateTimeOffset? date) =>
        date is { } value ? $"{prefix} {value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}" : string.Empty;
}
