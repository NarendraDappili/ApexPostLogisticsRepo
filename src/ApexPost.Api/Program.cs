using ApexPost.Api.Composition;
using ApexPost.Api.Errors;
using ApexPost.Api.Routing;
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

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
// {trackingNumber:trackingNumber} in a route template: anything that is not APX-######## never reaches an action.
builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<TrackingNumberRouteConstraint>(TrackingNumberRouteConstraint.Name));
builder.Services.AddApexPostPersistence(connectionString);

var app = builder.Build();
// First, so an error anywhere later in the pipeline becomes ProblemDetails (ApiExceptionHandler, then a plain 500).
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapControllers();
app.Run();

public partial class Program;
