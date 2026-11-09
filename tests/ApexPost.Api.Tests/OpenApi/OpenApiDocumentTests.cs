using System.Net;
using System.Text.Json;
using ApexPost.Api.OpenApi;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.Hosting;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C1: the built-in generator (AddOpenApi / MapOpenApi) describes the API at /openapi/v1.json, and the document
/// transformer gives it its title, description, version and contact. The document is served in Development only.
/// None of this needs a database: the document is built from the controllers' metadata.
/// </summary>
public sealed class OpenApiDocumentTests
{
    private const string DocumentPath = "/openapi/v1.json";

    [Fact]
    public async Task Development_serves_an_openapi_3_1_document()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(DocumentPath);
        var document = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task The_transformer_sets_title_description_version_and_contact()
    {
        var info = (await DocumentAsync()).GetProperty("info");
        var contact = info.GetProperty("contact");

        Assert.Equal("ApexPost Logistics API", info.GetProperty("title").GetString());
        Assert.Equal(ApiDocumentInfoTransformer.Version, info.GetProperty("version").GetString());
        Assert.Equal(ApiDocumentInfoTransformer.Description, info.GetProperty("description").GetString());
        Assert.Equal(("ApexPost Platform Team", "platform-team@apexpost.example", "https://developer.apexpost.example/support"),
            (contact.GetProperty("name").GetString(), contact.GetProperty("email").GetString(), contact.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task Every_endpoint_of_the_api_is_described_and_nothing_else()
    {
        var paths = (await DocumentAsync()).GetProperty("paths");

        var operations = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        // The eight Part B endpoints. /health is a hosting probe, not part of the contract.
        Assert.Equal(
        [
            "DELETE /api/shipments/{trackingNumber}",
            "GET /api/shipments",
            "GET /api/shipments/{trackingNumber}",
            "GET /api/shipments/{trackingNumber}/scans",
            "PATCH /api/shipments/{trackingNumber}/status",
            "POST /api/shipments",
            "POST /api/shipments/{trackingNumber}/scans",
            "PUT /api/shipments/{trackingNumber}",
        ], operations);
    }

    [Fact]
    public async Task Requests_and_responses_are_described_by_dtos_never_by_entities()
    {
        var schemas = (await DocumentAsync()).GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name).ToList();

        Assert.Contains("CreateShipmentRequest", schemas);
        Assert.Contains("ScanUploadRequest", schemas);
        Assert.Contains("ShipmentResponse", schemas);
        Assert.DoesNotContain("TrackedShipment", schemas);
        Assert.DoesNotContain("TrackingScan", schemas);
        Assert.DoesNotContain("Recipient", schemas);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_the_document_is_not_served(string environment)
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = factory.CreateClient();

        using var document = await client.GetAsync(DocumentPath);
        using var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal("application/problem+json", document.Content.Headers.ContentType!.MediaType);
        // Not publishing the document changes nothing else: the API itself answers as before.
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private static async Task<JsonElement> DocumentAsync()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(DocumentPath);
        response.EnsureSuccessStatusCode();
        return await response.JsonAsync();
    }
}
