using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Tests.Hosting;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// B4: [ApiController] validates every request DTO before an action runs and answers with a 400
/// ValidationProblemDetails keyed and worded by the JSON field names. Nothing reaches the service or the database.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class RequestValidationTests(SqlApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_validation_failure_is_a_400_validation_problem_details()
    {
        using var response = await _client.PostAsJsonAsync("/api/shipments", Shipment(trackingNumber: "APX-1"));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(("https://tools.ietf.org/html/rfc9110#section-15.5.1", "One or more validation errors occurred.", 400),
            (problem.GetProperty("type").GetString(), problem.GetProperty("title").GetString(), problem.GetProperty("status").GetInt32()));
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(["trackingNumber must be APX- followed by eight digits, for example APX-24000001."], Errors(problem, "trackingNumber"));
    }

    [Fact]
    public async Task An_empty_body_reports_every_required_field()
    {
        using var response = await _client.PostAsync("/api/shipments", ApiClient.RawJson("{}"));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["city", "recipientAddress", "recipientName", "recipientPhone", "trackingNumber", "weightKg"],
            problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["The weightKg field is required."], Errors(problem, "weightKg"));
    }

    [Theory]
    [InlineData("apx-24000001")]
    [InlineData("APX-2400000")]
    [InlineData("APX-240000011")]
    [InlineData("APX24000001")]
    [InlineData("APX-2400000A")]
    [InlineData("APX-24000001\n")]
    [InlineData(" APX-24000001")]
    [InlineData("APX-٢٤٠٠٠٠٠١")]
    public async Task The_tracking_number_must_match_apx_and_eight_digits(string trackingNumber)
    {
        var problem = await RejectedAsync(Shipment(trackingNumber: trackingNumber));

        Assert.Single(Errors(problem, "trackingNumber"));
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70.001)]
    [InlineData(500)]
    public async Task The_weight_must_be_between_0_1_and_70_kg(double weight)
    {
        var problem = await RejectedAsync(Shipment(weightKg: (decimal)weight));

        Assert.Equal(["weightKg must be between 0.1 and 70 kg."], Errors(problem, "weightKg"));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(70)]
    public async Task The_weight_limits_themselves_are_accepted(double weight)
    {
        using var response = await _client.PostAsJsonAsync("/api/shipments", Shipment(SqlApiFactory.NextTrackingNumber(), weightKg: (decimal)weight));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("recipientName")]
    [InlineData("recipientPhone")]
    [InlineData("recipientAddress")]
    [InlineData("city")]
    public async Task Each_recipient_field_is_required_and_cannot_be_blank(string field)
    {
        var blank = With(Shipment(), field, "   ");
        var missing = With(Shipment(), field, null);

        Assert.Equal([$"The {field} field is required."], Errors(await RejectedAsync(blank), field));
        Assert.Equal([$"The {field} field is required."], Errors(await RejectedAsync(missing), field));
    }

    [Theory]
    [InlineData("recipientName", 101)]
    [InlineData("recipientAddress", 201)]
    [InlineData("city", 81)]
    public async Task Recipient_fields_have_the_column_lengths(string field, int length)
    {
        var problem = await RejectedAsync(With(Shipment(), field, new string('x', length)));

        Assert.Single(Errors(problem, field));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("+1234567890123456")]
    [InlineData("020-ABC-0000")]
    [InlineData("020 7946 0000 ext 5")]
    [InlineData("44+ 20 7946 0000")]
    [InlineData("phone")]
    public async Task The_phone_number_must_be_dialable(string phone)
    {
        var problem = await RejectedAsync(Shipment(phone: phone));

        Assert.Equal(["recipientPhone must have 7 to 15 digits and may contain only a leading +, spaces, hyphens and brackets."], Errors(problem, "recipientPhone"));
    }

    [Fact]
    public async Task Update_has_the_same_rules_without_a_tracking_number()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var response = await _client.PutAsJsonAsync($"/api/shipments/{number}", new { recipientName = "", recipientPhone = "12", recipientAddress = "3 Quay Street", city = "Rivermouth", weightKg = 71m });
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["recipientName", "recipientPhone", "weightKg"], problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("{}", "The status field is required.")]
    [InlineData("""{ "status": null }""", "The status field is required.")]
    [InlineData("""{ "status": 42 }""", "status is not a known shipment status.")]
    public async Task A_status_update_needs_a_known_status(string json, string message)
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var response = await _client.PatchAsync($"/api/shipments/{number}/status", ApiClient.RawJson(json));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([message], Errors(problem, "status"));
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=51", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("status=42", "status")]
    public async Task The_list_query_is_validated_too(string query, string field)
    {
        using var response = await _client.GetAsync($"/api/shipments?{query}");
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(Errors(problem, field));
    }

    [Theory]
    [InlineData("/api/shipments/APX-00000001/status", "PATCH", """{ "status": "Lost" }""", "$.status")]
    [InlineData("/api/shipments", "POST", """{ "trackingNumber": """, "$.trackingNumber")]
    [InlineData("/api/shipments", "POST", """{ "weightKg": "heavy" }""", "$.weightKg")]
    public async Task Unreadable_json_names_the_path_without_internal_type_names(string path, string method, string json, string key)
    {
        using var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = ApiClient.RawJson(json) });
        var text = await response.Content.ReadAsStringAsync();
        var errors = JsonDocument.Parse(text).RootElement.GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(["The input was not valid."], Errors(JsonDocument.Parse(text).RootElement, key));
        Assert.DoesNotContain("ApexPost.", text);
        Assert.DoesNotContain("System.", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task An_empty_body_is_one_error_not_two(string body)
    {
        using var response = await _client.PostAsync("/api/shipments", ApiClient.RawJson(body));
        var errors = (await response.JsonAsync()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(errors.EnumerateObject());
        Assert.False(errors.TryGetProperty("request", out _));
    }

    [Fact]
    public async Task A_refused_create_stores_nothing()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.PostAsJsonAsync("/api/shipments", Shipment(number, weightKg: 71m))).Dispose();

        using var fetched = await _client.GetAsync($"/api/shipments/{number}");

        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    private async Task<JsonElement> RejectedAsync(Dictionary<string, object?> body)
    {
        using var response = await _client.PostAsJsonAsync("/api/shipments", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.JsonAsync();
    }

    private static Dictionary<string, object?> Shipment(string trackingNumber = "APX-00000001", string phone = "+44 20 7946 0000", decimal weightKg = 5m) => new()
    {
        ["trackingNumber"] = trackingNumber,
        ["recipientName"] = "Ama Mensah",
        ["recipientPhone"] = phone,
        ["recipientAddress"] = "12 Harbour Road",
        ["city"] = "Portside",
        ["weightKg"] = weightKg,
    };

    private static Dictionary<string, object?> With(Dictionary<string, object?> body, string field, object? value)
    {
        if (value is null) body.Remove(field);
        else body[field] = value;
        return body;
    }

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").TryGetProperty(field, out var errors) ? [.. errors.EnumerateArray().Select(error => error.GetString()!)] : [];
}
