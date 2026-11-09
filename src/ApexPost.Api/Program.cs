using ApexPost.Api.Composition;
using ApexPost.Api.Errors;
using ApexPost.Api.Routing;
using ApexPost.Api.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
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
});
// Every error response is ProblemDetails (application/problem+json) with the traceId and the request it answers.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Instance ??= $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}");
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
// {trackingNumber:trackingNumber} in a route template: anything that is not APX-######## never reaches an action.
builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<TrackingNumberRouteConstraint>(TrackingNumberRouteConstraint.Name));
builder.Services.AddApexPostPersistence(connectionString);

var app = builder.Build();
// First, so an exception anywhere later in the pipeline becomes ProblemDetails through ApiExceptionHandler.
app.UseExceptionHandler();
// Errors that end without a body (no route matches: 404; wrong method: 405) get a ProblemDetails body as well.
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapControllers();
app.Run();

public partial class Program;
