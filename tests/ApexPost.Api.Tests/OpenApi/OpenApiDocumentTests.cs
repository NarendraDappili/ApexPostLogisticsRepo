using System.Net;
using System.Text.Json;
using ApexPost.Api.OpenApi;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using ApexPost.Api.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C1 and C5: the built-in generator (AddOpenApi / MapOpenApi) describes each API version in its own document,
/// /openapi/v1.json and /openapi/v2.json, chosen by the Asp.Versioning API Explorer group names. The document
/// transformer gives each its title, description, version and contact; v1's description starts with its deprecation
/// notice and every v1 operation is marked deprecated. The documents are served in Development only. None of this
/// needs a database: the documents are built from the controllers' metadata.
/// </summary>
public sealed class OpenApiDocumentTests
{
    public static TheoryData<string> Versions => ["v1", "v2"];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Development_serves_an_openapi_3_1_document_per_version(string version)
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/openapi/{version}.json");
        var document = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task Every_api_version_has_a_document_and_there_is_no_other()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        var groups = factory.Services.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions.Select(description => description.GroupName);

        using var v3 = await client.GetAsync("/openapi/v3.json");

        Assert.Equal(["v1", "v2"], groups.Order(StringComparer.Ordinal));
        Assert.Equal(groups.Order(StringComparer.Ordinal), OpenApiRegistration.DocumentNames.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, v3.StatusCode);
    }

    [Theory]
    [InlineData("v1", "1.0")]
    [InlineData("v2", "2.0")]
    public async Task The_transformer_sets_title_version_and_contact(string version, string number)
    {
        var info = (await OpenApiMetadataTests.DocumentAsync(version)).GetProperty("info");
        var contact = info.GetProperty("contact");

        Assert.Equal(("ApexPost Logistics API", number), (info.GetProperty("title").GetString(), info.GetProperty("version").GetString()));
        Assert.EndsWith(ApiDocumentInfoTransformer.Description, info.GetProperty("description").GetString());
        Assert.Equal(("ApexPost Platform Team", "platform-team@apexpost.example", "https://developer.apexpost.example/support"),
            (contact.GetProperty("name").GetString(), contact.GetProperty("email").GetString(), contact.GetProperty("url").GetString()));
    }

    [Fact]
    public async Task V1_says_it_is_deprecated_until_when_and_what_to_use_instead()
    {
        var v1 = (await OpenApiMetadataTests.DocumentAsync("v1")).GetProperty("info").GetProperty("description").GetString()!;
        var v2 = (await OpenApiMetadataTests.DocumentAsync("v2")).GetProperty("info").GetProperty("description").GetString()!;

        Assert.StartsWith("**Deprecated since 9 November 2026.** This version stops answering after 31 May 2027. Use v2 instead.", v1);
        Assert.Contains(ApiVersions.PolicyUrl, v1);
        Assert.Equal(ApiDocumentInfoTransformer.Description, v2);
    }

    [Theory]
    [InlineData("v1", true)]
    [InlineData("v2", false)]
    public async Task Every_operation_of_v1_and_none_of_v2_is_deprecated(string version, bool deprecated)
    {
        var operations = OpenApiMetadataTests.AllOperations(await OpenApiMetadataTests.DocumentAsync(version)).ToList();

        // v2 also has the partner route (D3).
        Assert.Equal(version == "v1" ? 8 : 9, operations.Count);
        Assert.All(operations, operation => Assert.Equal(deprecated, operation.Value.TryGetProperty("deprecated", out var flag) && flag.GetBoolean()));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Each_document_describes_its_own_version_and_nothing_else(string version)
    {
        var operations = OpenApiMetadataTests.AllOperations(await OpenApiMetadataTests.DocumentAsync(version)).Select(operation => operation.Route).Order(StringComparer.Ordinal);

        // The eight Part B endpoints. /health is a hosting probe, not part of the contract.
        string[] expected =
        [
            $"DELETE /api/{version}/shipments/{{trackingNumber}}",
            $"GET /api/{version}/shipments",
            $"GET /api/{version}/shipments/{{trackingNumber}}",
            $"GET /api/{version}/shipments/{{trackingNumber}}/scans",
            $"PATCH /api/{version}/shipments/{{trackingNumber}}/status",
            $"POST /api/{version}/shipments",
            $"POST /api/{version}/shipments/{{trackingNumber}}/scans",
            $"PUT /api/{version}/shipments/{{trackingNumber}}",
            // v2 adds the read-only partner route (D3).
            .. version == "v2" ? ["GET /api/v2/partner/shipments/{trackingNumber}"] : Array.Empty<string>(),
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), operations);
    }

    [Theory]
    [InlineData("v1", new string[0], new[] { "RecipientResponse", "LatestScanResponse" })]
    [InlineData("v2", new[] { "RecipientResponse", "LatestScanResponse" }, new string[0])]
    public async Task Requests_and_responses_are_described_by_dtos_never_by_entities(string version, string[] present, string[] absent)
    {
        var schemas = OpenApiMetadataTests.SchemaNames(await OpenApiMetadataTests.DocumentAsync(version));

        Assert.Contains("CreateShipmentRequest", schemas);
        Assert.Contains("ScanUploadRequest", schemas);
        Assert.Contains("ShipmentResponse", schemas);
        Assert.All(present, name => Assert.Contains(name, schemas));
        Assert.All(absent, name => Assert.DoesNotContain(name, schemas));
        Assert.DoesNotContain("TrackedShipment", schemas);
        Assert.DoesNotContain("TrackingScan", schemas);
        Assert.DoesNotContain("Recipient", schemas);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_no_document_is_served(string environment)
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = factory.CreateClient();

        using var v1 = await client.GetAsync("/openapi/v1.json");
        using var v2 = await client.GetAsync("/openapi/v2.json");
        using var health = await client.GetAsync("/health");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (v1.StatusCode, v2.StatusCode));
        Assert.Equal("application/problem+json", v1.Content.Headers.ContentType!.MediaType);
        // Not publishing the documents changes nothing else: the API itself answers as before.
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
