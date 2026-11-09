using System.Collections.Concurrent;
using System.Text.Json;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C3, for both versions' documents: they carry what a client developer and a client generator need. Every operation
/// has a stable operationId (the action's route name), a tag, a summary and every status code it can return, each
/// described; every field and parameter is described from the XML comments; and the schemas say what the API really
/// accepts and returns (named enums, numbers as numbers, required request fields never null, the 201's Location
/// header). Only the enums differ by version: v1 responses write numbers, v2 responses names.
/// </summary>
public sealed class OpenApiMetadataTests
{
    private const string ProblemJson = "application/problem+json";
    private static readonly ConcurrentDictionary<string, Task<JsonElement>> Documents = new(StringComparer.Ordinal);

    public static TheoryData<string> Versions => ["v1", "v2"];

    // operationId, tag and every status code each operation declares, the same in both versions. The operationIds
    // are a published contract: a generated client's method names come from them, so a change here breaks clients.
    private static readonly (string Route, string OperationId, string Tag, int[] StatusCodes)[] OperationTable =
    [
        ("GET /shipments", "ListShipments", "Shipments", [200, 401, 400, 500]),
        ("GET /shipments/{trackingNumber}", "GetShipment", "Shipments", [200, 401, 404, 500]),
        ("POST /shipments", "CreateShipment", "Shipments", [201, 401, 403, 400, 409, 415, 500]),
        ("PUT /shipments/{trackingNumber}", "UpdateShipment", "Shipments", [200, 401, 403, 400, 404, 409, 415, 500]),
        ("PATCH /shipments/{trackingNumber}/status", "UpdateShipmentStatus", "Shipments", [200, 401, 403, 400, 404, 409, 415, 500]),
        ("DELETE /shipments/{trackingNumber}", "DeleteShipment", "Shipments", [204, 401, 403, 404, 409, 500]),
        ("GET /shipments/{trackingNumber}/scans", "GetScanHistory", "Scans", [200, 401, 404, 500]),
        ("POST /shipments/{trackingNumber}/scans", "UploadScans", "Scans", [200, 401, 403, 400, 404, 409, 415, 500]),
    ];

    public static TheoryData<string, string, string, string, int[]> Operations
    {
        get
        {
            var data = new TheoryData<string, string, string, string, int[]>();
            foreach (var version in new[] { "v1", "v2" })
            {
                foreach (var (route, operationId, tag, statusCodes) in OperationTable)
                {
                    data.Add(version, route.Replace(" /", $" /api/{version}/", StringComparison.Ordinal), operationId, tag, statusCodes);
                }
            }
            // The partner route (D3) is in v2 only; it takes an API key, so it has no 403.
            data.Add("v2", "GET /api/v2/partner/shipments/{trackingNumber}", "GetPartnerShipment", "Partner", [200, 401, 404, 500]);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Each_operation_has_its_operation_id_tag_summary_and_status_codes(string version, string route, string operationId, string tag, int[] statusCodes)
    {
        var operation = Operation(await DocumentAsync(version), route);

        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal([tag], operation.GetProperty("tags").EnumerateArray().Select(item => item.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        Assert.Equal(statusCodes.Select(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order(),
            operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order());
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Operation_ids_are_unique(string version)
    {
        var ids = AllOperations(await DocumentAsync(version)).Select(operation => operation.Value.GetProperty("operationId").GetString()).ToList();

        Assert.Equal(version == "v1" ? 8 : 9, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Every_response_is_described_and_every_error_is_problem_details(string version)
    {
        foreach (var (route, operation) in AllOperations(await DocumentAsync(version)))
        {
            foreach (var response in operation.GetProperty("responses").EnumerateObject())
            {
                var code = int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture);
                var description = response.Value.GetProperty("description").GetString();
                Assert.False(string.IsNullOrWhiteSpace(description), $"{route} {response.Name}");
                // The generator's default is the reason phrase ("Not Found"); ours says when the status happens.
                Assert.NotEqual(ReasonPhrases.GetReasonPhrase(code), description);

                if (code == 204) continue;
                var content = response.Value.GetProperty("content");
                var schema = Assert.Single(content.EnumerateObject());
                var reference = schema.Value.GetProperty("schema").TryGetProperty("$ref", out var refValue) ? refValue.GetString() : null;
                if (code >= 400)
                {
                    Assert.Equal(ProblemJson, schema.Name);
                    Assert.Equal(code == 400 ? "#/components/schemas/ValidationProblemDetails" : "#/components/schemas/ProblemDetails", reference);
                }
                else
                {
                    Assert.Equal("application/json", schema.Name);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task A_201_documents_its_location_header(string version)
    {
        var created = Operation(await DocumentAsync(version), $"POST /api/{version}/shipments").GetProperty("responses").GetProperty("201");
        var location = created.GetProperty("headers").GetProperty("Location");

        Assert.True(location.GetProperty("required").GetBoolean());
        Assert.Equal(("string", "uri"), (location.GetProperty("schema").GetProperty("type").GetString(), location.GetProperty("schema").GetProperty("format").GetString()));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Every_parameter_and_every_field_of_our_schemas_is_described(string version)
    {
        var document = await DocumentAsync(version);

        foreach (var (route, operation) in AllOperations(document))
        {
            foreach (var parameter in operation.TryGetProperty("parameters", out var parameters) ? parameters.EnumerateArray() : default)
            {
                Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("description").GetString()), $"{route} ?{parameter.GetProperty("name")}");
            }
            if (operation.TryGetProperty("requestBody", out var body))
            {
                Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("description").GetString()), $"{route} body");
            }
        }

        // ProblemDetails and ValidationProblemDetails are ASP.NET Core's (RFC 9457), not ours.
        var ours = document.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Where(schema => schema.Value.TryGetProperty("properties", out _) && !schema.Name.EndsWith("ProblemDetails", StringComparison.Ordinal));
        foreach (var schema in ours)
        {
            Assert.False(string.IsNullOrWhiteSpace(schema.Value.GetProperty("description").GetString()), schema.Name);
            foreach (var property in schema.Value.GetProperty("properties").EnumerateObject())
            {
                Assert.True(Description(property.Value) is { Length: > 0 }, $"{schema.Name}.{property.Name}");
            }
        }
    }

    [Theory]
    [InlineData("v1", "CreateShipmentRequest")]
    [InlineData("v1", "UpdateShipmentRequest")]
    [InlineData("v1", "UpdateStatusRequest")]
    [InlineData("v1", "ScanUploadItem")]
    [InlineData("v2", "CreateShipmentRequest")]
    [InlineData("v2", "UpdateShipmentRequest")]
    [InlineData("v2", "UpdateStatusRequest")]
    [InlineData("v2", "ScanUploadItem")]
    public async Task Every_request_field_has_an_example_for_try_it_out(string version, string schemaName)
    {
        var properties = Schema(await DocumentAsync(version), schemaName).GetProperty("properties");

        Assert.All(properties.EnumerateObject(), property => Assert.True(property.Value.TryGetProperty("examples", out _), $"{schemaName}.{property.Name}"));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Requests_take_enums_by_name_in_both_versions(string version)
    {
        var document = await DocumentAsync(version);

        // One shared schema per enum, listing the names.
        Assert.Equal(["Created", "PickedUp", "InTransit", "OutForDelivery", "Delivered", "Failed"], EnumValues(Schema(document, "TrackingStatus"), "string"));
        Assert.Equal(["Pickup", "InTransit", "Delivered", "Failed"], EnumValues(Schema(document, "TrackingScanType"), "string"));
        Assert.Equal("#/components/schemas/TrackingStatus", Reference(Property(document, "UpdateStatusRequest", "status")));
        Assert.Equal("#/components/schemas/TrackingScanType", Reference(Property(document, "ScanUploadItem", "scanType")));
        var statusFilter = Operation(document, $"GET /api/{version}/shipments").GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "status");
        Assert.Equal("#/components/schemas/TrackingStatus", Reference(statusFilter.GetProperty("schema")));
    }

    [Fact]
    public async Task V1_responses_write_enums_as_numbers_and_say_which_number_is_which()
    {
        var document = await DocumentAsync("v1");

        var status = Property(document, "ShipmentResponse", "status");
        Assert.Equal(["0", "1", "2", "3", "4", "5"], EnumValues(status, "integer"));
        Assert.Contains("1 PickedUp", status.GetProperty("description").GetString());
        Assert.Equal(["0", "1", "2", "3"], EnumValues(Property(document, "ScanResponse", "scanType"), "integer"));
        Assert.Equal(["0", "1", "2", "3", "4", "5"], EnumValues(Property(document, "ScanUploadResponse", "shipmentStatus"), "integer"));
    }

    [Fact]
    public async Task V2_responses_write_enums_by_name()
    {
        var document = await DocumentAsync("v2");

        Assert.Equal("#/components/schemas/TrackingStatus", Reference(Property(document, "ShipmentResponse", "status")));
        Assert.Equal("#/components/schemas/TrackingStatus", Reference(Property(document, "ScanUploadResponse", "shipmentStatus")));
        Assert.Equal("#/components/schemas/TrackingScanType", Reference(Property(document, "ScanResponse", "scanType")));
        Assert.Equal("#/components/schemas/TrackingScanType", Reference(Property(document, "LatestScanResponse", "scanType")));
    }

    [Fact]
    public async Task V2_nests_the_recipient_and_documents_latest_scan_as_nullable()
    {
        var document = await DocumentAsync("v2");
        var shipment = Schema(document, "ShipmentResponse").GetProperty("properties");

        Assert.Equal(["latestScan", "recipient", "status", "trackingNumber", "weightKg"], shipment.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("#/components/schemas/RecipientResponse", Reference(shipment.GetProperty("recipient")));
        Assert.Equal(["address", "city", "name", "phone"], Schema(document, "RecipientResponse").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["location", "scanType", "scannedAtUtc"], Schema(document, "LatestScanResponse").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var latestScan = shipment.GetProperty("latestScan").GetProperty("oneOf").EnumerateArray().ToList();
        Assert.Equal(2, latestScan.Count);
        Assert.Contains(latestScan, option => Types(option) is ["null"]);
        Assert.Contains(latestScan, option => Reference(option) == "#/components/schemas/LatestScanResponse");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Required_request_fields_are_never_null_but_a_nullable_response_field_is(string version)
    {
        var document = await DocumentAsync(version);

        foreach (var name in new[] { "CreateShipmentRequest", "UpdateShipmentRequest", "UpdateStatusRequest", "ScanUploadRequest", "ScanUploadItem" })
        {
            var schema = Schema(document, name);
            foreach (var required in schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!))
            {
                var property = schema.GetProperty("properties").GetProperty(required);
                Assert.False(property.TryGetProperty("oneOf", out _), $"{name}.{required}");
                Assert.DoesNotContain("null", Types(property));
            }
        }
        // Optional, and null when the courier wrote nothing.
        Assert.Contains("null", Types(Property(document, "ScanUploadItem", "notes")));
        Assert.Contains("null", Types(Property(document, "ScanResponse", "notes")));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Numbers_are_documented_as_numbers_never_as_strings(string version)
    {
        var offenders = new List<string>();
        Walk(await DocumentAsync(version), "$", offenders);

        Assert.Empty(offenders);
        static void Walk(JsonElement element, string path, List<string> offenders)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var types = Types(element);
                if ((types.Contains("number") || types.Contains("integer")) && types.Contains("string")) offenders.Add(path);
                foreach (var property in element.EnumerateObject()) Walk(property.Value, $"{path}.{property.Name}", offenders);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray()) Walk(item, $"{path}[{index++}]", offenders);
            }
        }
    }

    /// <summary>The version's document from a Development host; fetched once per test run.</summary>
    internal static Task<JsonElement> DocumentAsync(string version) => Documents.GetOrAdd(version, async name =>
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/openapi/{name}.json");
        response.EnsureSuccessStatusCode();
        return await response.JsonAsync();
    });

    internal static JsonElement Operation(JsonElement document, string route)
    {
        var (method, path) = (route[..route.IndexOf(' ')], route[(route.IndexOf(' ') + 1)..]);
        return document.GetProperty("paths").GetProperty(path).GetProperty(method.ToLowerInvariant());
    }

    internal static IEnumerable<(string Route, JsonElement Value)> AllOperations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => ($"{operation.Name.ToUpperInvariant()} {path.Name}", operation.Value)));

    internal static JsonElement Schema(JsonElement document, string name) => document.GetProperty("components").GetProperty("schemas").GetProperty(name);

    internal static List<string> SchemaNames(JsonElement document) => [.. document.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name)];

    private static JsonElement Property(JsonElement document, string schema, string property) => Schema(document, schema).GetProperty("properties").GetProperty(property);

    private static string? Reference(JsonElement schema) => schema.TryGetProperty("$ref", out var reference) ? reference.GetString() : null;

    // A property pointing to a shared schema carries its description next to the $ref, or inside a nullable oneOf.
    private static string? Description(JsonElement property) =>
        property.TryGetProperty("description", out var description) ? description.GetString()
        : property.TryGetProperty("oneOf", out var oneOf) ? oneOf.EnumerateArray().Select(Description).FirstOrDefault(text => text is not null)
        : null;

    private static List<string> EnumValues(JsonElement schema, string type)
    {
        Assert.Equal([type], Types(schema));
        return [.. schema.GetProperty("enum").EnumerateArray().Select(value => value.ToString())];
    }

    // A schema's "type" is a name or a list of names. (A "properties" object can also hold a field called "type".)
    private static List<string> Types(JsonElement schema) => !schema.TryGetProperty("type", out var type)
        ? []
        : type.ValueKind switch
        {
            JsonValueKind.String => [type.GetString()!],
            JsonValueKind.Array => [.. type.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)],
            _ => [],
        };
}
