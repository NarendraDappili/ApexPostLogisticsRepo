using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C3 "Try it out", automated: every operation is called the way Swagger UI calls it, with a body built only from the
/// document's own examples, against a real database. Each answer must be a status the operation documents, and a
/// success body must have exactly the fields its documented schema lists. Only the tracking numbers and scan IDs
/// are fresh, so the examples can be sent more than once.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class TryItOutTests(SqlApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Every_operation_answers_its_documented_examples_with_documented_statuses_and_shapes()
    {
        var document = await DocumentAsync();
        var number = SqlApiFactory.NextTrackingNumber();
        var deletable = SqlApiFactory.NextTrackingNumber();
        var unknown = SqlApiFactory.NextTrackingNumber();
        var create = Example(document, "CreateShipmentRequest");
        var update = Example(document, "UpdateShipmentRequest");
        var status = Example(document, "UpdateStatusRequest");
        var upload = Example(document, "ScanUploadRequest");

        // The successes, in the order a user trying the API would go.
        await Expect(document, "POST /api/v1/shipments", null, With(create, "trackingNumber", number), 201);
        await Expect(document, "POST /api/v1/shipments", null, With(create, "trackingNumber", deletable), 201);
        await Expect(document, "GET /api/v1/shipments/{trackingNumber}", number, null, 200);
        await Expect(document, "GET /api/v1/shipments", null, null, 200, "?page=1&pageSize=20&status=Created&city=Portside");
        await Expect(document, "PUT /api/v1/shipments/{trackingNumber}", number, update, 200);
        await Expect(document, "PATCH /api/v1/shipments/{trackingNumber}/status", number, status, 200);
        await Expect(document, "POST /api/v1/shipments/{trackingNumber}/scans", number, WithFreshScanIds(upload), 200);
        await Expect(document, "GET /api/v1/shipments/{trackingNumber}/scans", number, null, 200);
        await Expect(document, "DELETE /api/v1/shipments/{trackingNumber}", deletable, null, 204);

        // At least one failure each.
        await Expect(document, "GET /api/v1/shipments", null, null, 400, "?pageSize=0");
        await Expect(document, "GET /api/v1/shipments/{trackingNumber}", unknown, null, 404);
        await Expect(document, "POST /api/v1/shipments", null, With(create, "trackingNumber", number), 409);
        await Expect(document, "POST /api/v1/shipments", null, new JsonObject(), 400);
        await Expect(document, "PUT /api/v1/shipments/{trackingNumber}", unknown, update, 404);
        await Expect(document, "PATCH /api/v1/shipments/{trackingNumber}/status", number, status, 409);
        await Expect(document, "DELETE /api/v1/shipments/{trackingNumber}", number, null, 409);
        await Expect(document, "GET /api/v1/shipments/{trackingNumber}/scans", unknown, null, 404);
        await Expect(document, "POST /api/v1/shipments/{trackingNumber}/scans", number, new JsonObject { ["scans"] = new JsonArray() }, 400);

        // 415: the body as text/plain.
        using var wrongType = await _client.PostAsync("/api/v1/shipments", new StringContent(With(create, "trackingNumber", unknown).ToJsonString(), Encoding.UTF8, "text/plain"));
        Assert.Equal(415, (int)wrongType.StatusCode);
        Assert.Contains("415", Declared(OpenApiMetadataTests.Operation(document, "POST /api/v1/shipments")));
    }

    private async Task Expect(JsonElement document, string route, string? trackingNumber, JsonObject? body, int expected, string query = "")
    {
        var operation = OpenApiMetadataTests.Operation(document, route);
        var method = new HttpMethod(route[..route.IndexOf(' ')]);
        var path = route[(route.IndexOf(' ') + 1)..].Replace("{trackingNumber}", trackingNumber, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(method, path + query) { Content = body is null ? null : JsonContent.Create(body) };

        using var response = await _client.SendAsync(request);
        var status = (int)response.StatusCode;

        Assert.True(status == expected, $"{route}: expected {expected}, got {status}: {await response.Content.ReadAsStringAsync()}");
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), Declared(operation));
        if (status >= 400)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        else if (status != 204)
        {
            var schema = Resolve(document, operation.GetProperty("responses").GetProperty(status.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            AssertShape(document, schema, await response.JsonAsync(), route);
        }
    }

    // The response has exactly the documented fields; lists are checked through their first item.
    private static void AssertShape(JsonElement document, JsonElement schema, JsonElement value, string at)
    {
        if (schema.TryGetProperty("items", out var items))
        {
            Assert.Equal(JsonValueKind.Array, value.ValueKind);
            if (value.GetArrayLength() > 0) AssertShape(document, Resolve(document, items), value[0], at + "[0]");
            return;
        }
        if (!schema.TryGetProperty("properties", out var properties)) return;

        Assert.Equal(properties.EnumerateObject().Select(property => property.Name).Order(), value.EnumerateObject().Select(property => property.Name).Order());
        foreach (var property in properties.EnumerateObject())
        {
            var nested = Resolve(document, property.Value);
            if (value.GetProperty(property.Name).ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                AssertShape(document, nested, value.GetProperty(property.Name), $"{at}.{property.Name}");
            }
        }
    }

    private static JsonElement Resolve(JsonElement document, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? OpenApiMetadataTests.Schema(document, reference.GetString()!["#/components/schemas/".Length..])
            : schema;

    private static List<string> Declared(JsonElement operation) => [.. operation.GetProperty("responses").EnumerateObject().Select(response => response.Name)];

    // A body made of the documented examples, as Swagger UI fills it in.
    private static JsonObject Example(JsonElement document, string schemaName)
    {
        var body = new JsonObject();
        foreach (var property in OpenApiMetadataTests.Schema(document, schemaName).GetProperty("properties").EnumerateObject())
        {
            body[property.Name] = property.Value.TryGetProperty("examples", out var examples)
                ? JsonNode.Parse(examples[0].GetRawText())
                : property.Value.TryGetProperty("items", out var items) && items.TryGetProperty("$ref", out var itemRef)
                    ? new JsonArray(Example(document, itemRef.GetString()!["#/components/schemas/".Length..]))
                    : null;
        }
        return body;
    }

    private static JsonObject With(JsonObject body, string name, string value)
    {
        var copy = (JsonObject)body.DeepClone();
        copy[name] = value;
        return copy;
    }

    private static JsonObject WithFreshScanIds(JsonObject upload)
    {
        var copy = (JsonObject)upload.DeepClone();
        foreach (var scan in copy["scans"]!.AsArray()) scan!["scanId"] = $"scan::{Guid.NewGuid()}";
        return copy;
    }

    private async Task<JsonElement> DocumentAsync()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.JsonAsync();
    }
}
