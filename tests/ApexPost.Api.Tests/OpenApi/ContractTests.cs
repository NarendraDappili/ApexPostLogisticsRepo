using System.Text.Json.Nodes;
using ApexPost.Api.Tests.Hosting;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C7: the committed contracts/v1.json and contracts/v2.json are exactly what the API describes today. A change to a
/// controller, DTO or XML comment that changes a document fails here until the contracts are exported again, so every
/// contract change shows up as a reviewed diff of contracts/*.json (and the client is regenerated from it).
/// </summary>
public sealed class ContractTests
{
    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task The_committed_contract_matches_the_document_the_api_serves(string version)
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        var live = Normalised(await client.GetStringAsync($"/openapi/{version}.json"));
        var committed = Normalised(await File.ReadAllTextAsync(RepositoryFiles.PathOf("contracts", $"{version}.json")));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(committed), JsonNode.Parse(live)),
            $"contracts/{version}.json is out of date. Run ./contracts/export-contracts.ps1, review the diff, record the change in VERSIONING.md "
            + "and regenerate the client (dotnet nswag run src/ApexPost.Client/nswag.json).");
    }

    [Fact]
    public async Task The_contracts_differ_only_where_versioning_md_says_v2_differs()
    {
        var v1 = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryFiles.PathOf("contracts", "v1.json")))!;
        var v2 = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryFiles.PathOf("contracts", "v2.json")))!;

        // The requests are the same in both versions.
        foreach (var request in new[] { "CreateShipmentRequest", "UpdateShipmentRequest", "UpdateStatusRequest", "ScanUploadRequest", "ScanUploadItem", "TrackingStatus", "TrackingScanType", "ProblemDetails", "ValidationProblemDetails" })
        {
            Assert.True(JsonNode.DeepEquals(v1["components"]!["schemas"]![request], v2["components"]!["schemas"]![request]), request);
        }
        // The operations are the same, path for path, apart from the version segment, and v2 adds the partner route.
        Assert.Equal(
            v1["paths"]!.AsObject().Select(path => path.Key.Replace("/api/v1/", "/api/", StringComparison.Ordinal)).Append("/api/partner/shipments/{trackingNumber}").Order(StringComparer.Ordinal),
            v2["paths"]!.AsObject().Select(path => path.Key.Replace("/api/v2/", "/api/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        // The responses are where they differ (VERSIONING.md).
        Assert.False(JsonNode.DeepEquals(v1["components"]!["schemas"]!["ShipmentResponse"], v2["components"]!["schemas"]!["ShipmentResponse"]));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task No_description_has_a_placeholder_swagger_ui_would_drop_as_html(string version)
    {
        // Descriptions are Markdown. Swagger UI drops <token> or <key> as an unknown HTML tag, so a placeholder in
        // angle brackets must sit inside a `code span`, where it is shown as written.
        var document = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryFiles.PathOf("contracts", $"{version}.json")))!;
        var offenders = Descriptions(document)
            .Where(description => System.Text.RegularExpressions.Regex.IsMatch(
                System.Text.RegularExpressions.Regex.Replace(description, "`[^`]*`", string.Empty), "<[A-Za-z/!]"))
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> Descriptions(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(property => property.Key == "description" && property.Value is JsonValue value && value.TryGetValue<string>(out var text)
            ? [text]
            : Descriptions(property.Value)),
        JsonArray array => array.SelectMany(Descriptions),
        _ => [],
    };

    // The export script writes line breaks inside descriptions as \n, so the files are the same on every operating
    // system; a Windows checkout of the XML comments yields \r\n. The file itself may also be checked out with CRLF.
    private static string Normalised(string json) => json.Replace(@"\r\n", @"\n", StringComparison.Ordinal);
}
