using ApexPost.Api.Versioning;
using Asp.Versioning;

namespace ApexPost.Api.Composition;

/// <summary>
/// URL-segment versioning (/api/v1/..., /api/v2/...). Every response reports the supported and deprecated versions
/// (api-supported-versions, api-deprecated-versions), and v1's responses also carry its Deprecation and Sunset dates
/// with a link to the versioning policy, so a client learns of the plan from any call it already makes.
/// </summary>
internal static class ApiVersioningRegistration
{
    public static IServiceCollection AddApexPostApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
                options.ReportApiVersions = true;
                options.Policies.Deprecate(ApiVersions.V1)
                    .Effective(ApiVersions.V1DeprecatedOn)
                    .Link(ApiVersions.PolicyUrl)
                    .Title("ApexPost API versions: moving from v1 to v2")
                    .Type("text/html");
                options.Policies.Sunset(ApiVersions.V1)
                    .Effective(ApiVersions.V1SunsetOn)
                    .Link(ApiVersions.PolicyUrl)
                    .Title("ApexPost API versions: moving from v1 to v2")
                    .Type("text/html");
            })
            .AddMvc()
            // Each version is its own API Explorer group, "v1" and "v2": the OpenAPI documents are built per group. The
            // documents show the concrete /api/v1/... paths rather than a {version} parameter.
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V";
                options.SubstituteApiVersionInUrl = true;
                options.SubstitutionFormat = "V";
            });
        return services;
    }
}
