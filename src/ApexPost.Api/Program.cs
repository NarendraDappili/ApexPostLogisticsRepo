using ApexPost.Api.Composition;
using ApexPost.Api.Errors;
using ApexPost.Api.OpenApi;
using ApexPost.Api.Routing;
using ApexPost.Api.Security;
using ApexPost.Api.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Rewrite;
using ApexPost.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// The API works on the Week 3 SQL Server database. In Development the connection string is the same user-secret
// that ApexPost.Web reads (dotnet user-secrets set "ConnectionStrings:ApexPostDb" "..."); elsewhere it comes from
// deployment configuration. No connection string is committed.
var connectionString = builder.Configuration.GetConnectionString(ApexPostDesignTimeDbContextFactory.ConnectionStringName);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:ApexPostDb is not configured. In Development set it with dotnet user-secrets; see README.md.");
}

// Validation errors are keyed and worded by the JSON names a client sends (recipientPhone, scans[0].scannedAtUtc),
// the same names the service uses for the errors it finds.
builder.Services.AddControllers(options =>
    {
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
        options.ModelMetadataDetailsProviders.Add(new JsonDisplayNameProvider());
        // Every DTO field states [Required] itself. The implicit rule would only add "The request field is required."
        // to every unreadable body, next to the error that says what is actually wrong.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    // A body that is not valid JSON, or a value of the wrong type, is reported against its JSON path ("$.status") as
    // "The input was not valid." The serializer's own text names internal types such as ApexPost.Domain.Enums.
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false);
// Every error response is ProblemDetails (application/problem+json) with the traceId and the request it answers.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}");
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
// {trackingNumber:trackingNumber} in a route template: anything that is not APX-######## never reaches an action.
builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<TrackingNumberRouteConstraint>(TrackingNumberRouteConstraint.Name));
builder.Services.AddApexPostPersistence(connectionString);
// /api/v1 (deprecated, the Part B contract) and /api/v2. Both versions call the same ShipmentTrackingService; only
// their response DTOs differ.
builder.Services.AddApexPostApiVersioning();
// The built-in generator describes each version in its own document: /openapi/v1.json and /openapi/v2.json.
builder.Services.AddApexPostOpenApi();
// JWT bearer tokens (issuer, audience, lifetime, signature) from Authentication:Schemes:Bearer; every endpoint needs
// an authenticated caller unless it allows anonymous ones (SECURITY.md).
builder.Services.AddApexPostAuthentication(builder.Configuration);
// The partner route takes an API key instead (PartnerApi:Keys, user-secrets in Development; SECURITY.md).
builder.Services.AddPartnerApiKeys(builder.Configuration);

var app = builder.Build();
// Read the partner keys now, so a malformed key stops the start rather than the first partner request.
_ = app.Services.GetRequiredService<PartnerApiKeys>();
// First, so an exception anywhere later in the pipeline becomes ProblemDetails through ApiExceptionHandler.
app.UseExceptionHandler();
// Errors that end without a body (no route matches: 404; wrong method: 405) get a ProblemDetails body as well.
app.UseStatusCodePages();
app.UseHttpsRedirection();
// Part B published /api/shipments without a version. Those URLs keep answering as v1, with v1's deprecation and
// sunset headers, until v1 is retired. A rewrite, not a second set of endpoints: it is not in any document. Routing
// must come after it, so the rewritten path is the one matched.
app.UseRewriter(new RewriteOptions().AddRewrite("^api/shipments(/.*)?$", "api/v1/shipments$1", skipRemainingRules: true));
app.UseRouting();
// Mapping an endpoint adds no middleware, so the endpoints below still run after authentication and authorization,
// which come last before them.
// A probe for the hosting platform, not part of the client contract, so it stays out of the OpenAPI document (and
// out of the generated client). It answers everyone, without a token.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).ExcludeFromDescription().AllowAnonymous();
// Mapped before the Swagger UI below asks which API versions the controllers describe. The actions marked
// [RequirePartnerApiKey] get the endpoint filter that checks X-Api-Key.
app.MapControllers().RequirePartnerApiKeysWhereMarked();
// The documents and the Swagger UI page are served in Development only. Not publishing them elsewhere is a choice
// about what to publish, not a security control: every endpoint answers the same with or without them.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    // A stand-in for an identity provider; never mapped outside Development (SECURITY.md).
    app.MapDevelopmentTokens();
    // Swagger UI is only a viewer: it renders the built-in documents above and generates nothing itself. Its
    // document dropdown lists every API version, newest (and so the default) first: "v2", "v1 (deprecated)".
    app.UseSwaggerUI(options =>
    {
        foreach (var version in app.DescribeApiVersions().OrderByDescending(description => description.ApiVersion))
        {
            options.SwaggerEndpoint($"/openapi/{version.GroupName}.json", version.IsDeprecated ? $"{version.GroupName} (deprecated)" : version.GroupName);
        }
        options.RoutePrefix = "swagger";
        options.DocumentTitle = ApiDocumentInfoTransformer.Title;
        // Shows each operationId (the action's route name, and the method name in a generated client).
        options.DisplayOperationId();
    });
}
// After routing, so each endpoint's [Authorize] and [AllowAnonymous] are known, and after Swagger UI: the fallback
// policy (an authenticated caller) also applies to requests that match no endpoint, and the page that lets a
// developer enter a token cannot itself require one.
app.UseAuthentication();
app.UseAuthorization();
app.Run();

/// <summary>The API host. Public so the integration tests can start it with WebApplicationFactory.</summary>
public partial class Program;
