using System.Text.Json;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C3: the document carries what a client developer and a client generator need. Every operation has a stable
/// operationId (the action's route name), a tag, a summary and every status code it can return, each described; every
/// field and parameter is described from the XML comments; and the schemas say what the API really accepts and
/// returns (named enums, numbers as numbers, required request fields never null, the 201's Location header).
/// </summary>
public sealed class OpenApiMetadataTests
{
    private const string ProblemJson = "application/problem+json";

    // operationId, tag and every status code each operation declares. The operationIds are a published contract:
    // a generated client's method names come from them, so a change here breaks clients.
    public static TheoryData<string, string, string, int[]> Operations => new()
    {
        { "GET /api/v1/shipments", "ListShipments", "Shipments", [200, 400, 500] },
        { "GET /api/v1/shipments/{trackingNumber}", "GetShipment", "Shipments", [200, 404, 500] },
        { "POST /api/v1/shipments", "CreateShipment", "Shipments", [201, 400, 409, 415, 500] },
        { "PUT /api/v1/shipments/{trackingNumber}", "UpdateShipment", "Shipments", [200, 400, 404, 409, 415, 500] },
        { "PATCH /api/v1/shipments/{trackingNumber}/status", "UpdateShipmentStatus", "Shipments", [200, 400, 404, 409, 415, 500] },
        { "DELETE /api/v1/shipments/{trackingNumber}", "DeleteShipment", "Shipments", [204, 404, 409, 500] },
        { "GET /api/v1/shipments/{trackingNumber}/scans", "GetScanHistory", "Scans", [200, 404, 500] },
        { "POST /api/v1/shipments/{trackingNumber}/scans", "UploadScans", "Scans", [200, 400, 404, 409, 415, 500] },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Each_operation_has_its_operation_id_tag_summary_and_status_codes(string route, string operationId, string tag, int[] statusCodes)
    {
        var operation = Operation(await DocumentAsync(), route);

        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal([tag], operation.GetProperty("tags").EnumerateArray().Select(item => item.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        Assert.Equal(statusCodes.Select(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order(),
            operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order());
    }

    [Fact]
    public async Task Operation_ids_are_unique()
    {
        var ids = AllOperations(await DocumentAsync()).Select(operation => operation.Value.GetProperty("operationId").GetString()).ToList();

        Assert.Equal(8, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Every_response_is_described_and_every_error_is_problem_details()
    {
        foreach (var (route, operation) in AllOperations(await DocumentAsync()))
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

    [Fact]
    public async Task A_201_documents_its_location_header()
    {
        var created = Operation(await DocumentAsync(), "POST /api/v1/shipments").GetProperty("responses").GetProperty("201");
        var location = created.GetProperty("headers").GetProperty("Location");

        Assert.True(location.GetProperty("required").GetBoolean());
        Assert.Equal(("string", "uri"), (location.GetProperty("schema").GetProperty("type").GetString(), location.GetProperty("schema").GetProperty("format").GetString()));
    }

    [Fact]
    public async Task Every_parameter_and_every_field_of_our_schemas_is_described()
    {
        var document = await DocumentAsync();

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
                Assert.True(property.Value.TryGetProperty("description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()),
                    $"{schema.Name}.{property.Name}");
            }
        }
    }

    [Theory]
    [InlineData("CreateShipmentRequest")]
    [InlineData("UpdateShipmentRequest")]
    [InlineData("UpdateStatusRequest")]
    [InlineData("ScanUploadItem")]
    public async Task Every_request_field_has_an_example_for_try_it_out(string schemaName)
    {
        var properties = Schema(await DocumentAsync(), schemaName).GetProperty("properties");

        Assert.All(properties.EnumerateObject(), property => Assert.True(property.Value.TryGetProperty("examples", out _), $"{schemaName}.{property.Name}"));
    }

    [Fact]
    public async Task Enums_are_named_where_names_travel_and_numbered_where_numbers_do()
    {
        var document = await DocumentAsync();

        // Request fields and the status filter take names: one shared schema per enum.
        Assert.Equal(["Created", "PickedUp", "InTransit", "OutForDelivery", "Delivered", "Failed"], EnumValues(Schema(document, "TrackingStatus"), "string"));
        Assert.Equal(["Pickup", "InTransit", "Delivered", "Failed"], EnumValues(Schema(document, "TrackingScanType"), "string"));
        Assert.Equal("#/components/schemas/TrackingStatus", Schema(document, "UpdateStatusRequest").GetProperty("properties").GetProperty("status").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/TrackingScanType", Schema(document, "ScanUploadItem").GetProperty("properties").GetProperty("scanType").GetProperty("$ref").GetString());
        var statusFilter = Operation(document, "GET /api/v1/shipments").GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "status");
        Assert.Equal("#/components/schemas/TrackingStatus", statusFilter.GetProperty("schema").GetProperty("$ref").GetString());

        // v1 responses write numbers, and say which number is which.
        var status = Schema(document, "ShipmentResponse").GetProperty("properties").GetProperty("status");
        Assert.Equal(["0", "1", "2", "3", "4", "5"], EnumValues(status, "integer"));
        Assert.Contains("1 PickedUp", status.GetProperty("description").GetString());
        Assert.Equal(["0", "1", "2", "3"], EnumValues(Schema(document, "ScanResponse").GetProperty("properties").GetProperty("scanType"), "integer"));
    }

    [Fact]
    public async Task Required_request_fields_are_never_null_but_a_nullable_response_field_is()
    {
        var document = await DocumentAsync();

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
        Assert.Contains("null", Types(Schema(document, "ScanUploadItem").GetProperty("properties").GetProperty("notes")));
        Assert.Contains("null", Types(Schema(document, "ScanResponse").GetProperty("properties").GetProperty("notes")));
    }

    [Fact]
    public async Task Numbers_are_documented_as_numbers_never_as_strings()
    {
        var offenders = new List<string>();
        Walk((await DocumentAsync()), "$", offenders);

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

    internal static async Task<JsonElement> DocumentAsync()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.JsonAsync();
    }

    internal static JsonElement Operation(JsonElement document, string route)
    {
        var (method, path) = (route[..route.IndexOf(' ')], route[(route.IndexOf(' ') + 1)..]);
        return document.GetProperty("paths").GetProperty(path).GetProperty(method.ToLowerInvariant());
    }

    internal static IEnumerable<(string Route, JsonElement Value)> AllOperations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => ($"{operation.Name.ToUpperInvariant()} {path.Name}", operation.Value)));

    internal static JsonElement Schema(JsonElement document, string name) => document.GetProperty("components").GetProperty("schemas").GetProperty(name);

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
