using System.Net;
using System.Text.Json;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// D8: JSON and errors. The app reads and writes with one source-generated context whose conventions match the v2
/// API. Error bodies (ProblemDetails, ValidationProblemDetails) become a message the courier can act on, without
/// server internals. Missing, null, unknown and malformed content never crashes the app: it becomes an empty value or
/// an "unreadable answer" error. The bodies below are the ones the API really sends (API-NOTES.md).
/// </summary>
public sealed class ErrorAndJsonHandlingTests : IDisposable
{
    private const string TraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private readonly CourierAppRun _app = new();
    private readonly StubServer _server = new();

    public void Dispose()
    {
        _app.Dispose();
        _server.Dispose();
    }

    [Fact]
    public void The_shared_options_follow_the_v2_api_conventions()
    {
        var options = CourierJsonContext.SharedOptions;

        Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal("\"InTransit\"", JsonSerializer.Serialize(ScanType.InTransit, CourierJsonContext.Default.ScanType));
        Assert.Equal(ScanType.Delivered, JsonSerializer.Deserialize("\"delivered\"", CourierJsonContext.Default.ScanType));
        Assert.Same(CourierJsonContext.Default, options.TypeInfoResolver);
    }

    [Fact]
    public async Task A_validation_problem_names_each_field_the_server_refused()
    {
        Answer(HttpStatusCode.BadRequest, $$"""
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": {
                "scans[0].scannedAtUtc": ["The scan time cannot be more than 5 minutes in the future."],
                "scans[1].location": ["The location field is required."]
              },
              "instance": "POST /api/v2/shipments/APX-30000001/scans",
              "traceId": "{{TraceId}}"
            }
            """);

        var exception = await UploadFailsAsync<ApexApiException>();

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(
            "The server did not accept the data: scans[0].scannedAtUtc: The scan time cannot be more than 5 minutes in the future.; scans[1].location: The location field is required.",
            exception.Message);
        Assert.Equal(2, exception.Problem!.Errors!.Count);
        Assert.Equal(TraceId, exception.Problem.TraceId);
    }

    [Fact]
    public async Task Only_the_first_three_validation_errors_are_listed()
    {
        Answer(HttpStatusCode.BadRequest, """{ "status": 400, "errors": { "a": ["A."], "b": ["B."], "c": ["C."], "d": ["D."], "e": ["E."] } }""");

        var exception = await UploadFailsAsync<ApexApiException>();

        Assert.Equal("The server did not accept the data: a: A.; b: B.; c: C. (and 2 more)", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "The access token has expired. Sign in again to get a new one.", "Please sign in again. The access token has expired. Sign in again to get a new one.")]
    [InlineData(HttpStatusCode.Forbidden, "A courier can upload scans only under their own courierId, CR-1042. This upload has scans under CR-2001.", "Not allowed. A courier can upload scans only under their own courierId, CR-1042. This upload has scans under CR-2001.")]
    [InlineData(HttpStatusCode.NotFound, "No shipment has the tracking number APX-30000001.", "No shipment has the tracking number APX-30000001.")]
    [InlineData(HttpStatusCode.Conflict, "Another upload for this shipment was saved at the same moment. Send the upload again.", "Another upload for this shipment was saved at the same moment. Send the upload again.")]
    public async Task A_problems_detail_is_what_the_courier_reads(HttpStatusCode status, string detail, string message)
    {
        Answer(status, $$"""{ "title": "Some title", "status": {{(int)status}}, "detail": "{{detail}}", "traceId": "{{TraceId}}" }""");

        var exception = await UploadFailsAsync<ApexApiException>();

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public async Task A_server_error_gives_only_a_reference_never_the_servers_internals()
    {
        // In Development the API adds the exception and its stack trace (B5); the courier must never see them.
        Answer(HttpStatusCode.InternalServerError, $$"""
            {
              "title": "An unexpected error occurred.",
              "status": 500,
              "detail": "SqlException: Cannot open database \"ApexPostDb\" requested by the login.",
              "exception": { "type": "Microsoft.Data.SqlClient.SqlException", "message": "Login failed.", "stackTrace": "at ApexPost..." },
              "traceId": "{{TraceId}}"
            }
            """);

        var exception = await UploadFailsAsync<ApexApiException>();

        Assert.Equal($"The server had a problem (reference {TraceId}). Your scans are saved on this device and will be sent on the next sync.", exception.Message);
        Assert.DoesNotContain("Sql", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<html><body><h1>502 Bad Gateway</h1></body></html>", "text/html")]
    [InlineData("", "text/plain")]
    [InlineData("{ \"title\": ", "application/problem+json")]
    [InlineData("[1, 2, 3]", "application/json")]
    public async Task An_error_body_that_is_not_problem_details_still_gives_a_message(string body, string mediaType)
    {
        _server.Respond = _ => StubServer.Json(HttpStatusCode.Forbidden, body, mediaType);
        using var host = Host();

        var exception = await UploadFailsAsync<ApexApiException>(host);

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Null(exception.Problem);
        Assert.Equal("Not allowed. Your account may not do this.", exception.Message);
    }

    [Fact]
    public async Task Unknown_fields_are_ignored_and_missing_or_null_ones_become_empty()
    {
        Answer(HttpStatusCode.OK, """
            {
              "accepted": "2",
              "rejected": 1,
              "rejections": [ { "scanId": "scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10", "reason": null }, null ],
              "shipmentStatus": null,
              "addedInApiV3": { "anything": [1, 2] },
              "ShipmentEta": "2026-11-10"
            }
            """);
        using var host = Host();

        var result = await Upload(host);

        Assert.Equal((2, 0, 1, null), (result.Accepted, result.DuplicatesSkipped, result.Rejected, result.ShipmentStatus));
        var rejection = Assert.Single(result.Rejections!);
        Assert.Equal(("scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10", null), (rejection.ScanId, rejection.Reason));
    }

    [Fact]
    public async Task Field_names_are_read_in_any_case()
    {
        Answer(HttpStatusCode.OK, """{ "Accepted": 1, "DUPLICATESSKIPPED": 2, "Rejected": 0, "ShipmentStatus": "InTransit" }""");
        using var host = Host();

        var result = await Upload(host);

        Assert.Equal((1, 2, "InTransit"), (result.Accepted, result.DuplicatesSkipped, result.ShipmentStatus));
        Assert.Empty(result.Rejections!);
    }

    [Theory]
    [InlineData("{ \"accepted\": ")]
    [InlineData("<html>Welcome to the hotel Wi-Fi</html>")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{ \"accepted\": \"many\" }")]
    public async Task A_malformed_success_body_is_an_unreadable_answer_not_a_crash(string body)
    {
        _server.Respond = _ => StubServer.Json(HttpStatusCode.OK, body);
        using var host = Host();

        var exception = await UploadFailsAsync<ApexApiException>(host);

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("The server's answer could not be read. Nothing was changed on this device; try again later.", exception.Message);
    }

    [Fact]
    public async Task A_sign_in_answer_without_a_token_is_unreadable()
    {
        Answer(HttpStatusCode.OK, """{ "tokenType": "Bearer", "expiresAtUtc": "2026-11-09T13:00:00Z" }""");
        using var host = Host();

        var exception = await Assert.ThrowsAsync<ApexApiException>(() => host.Services.GetRequiredService<ApexApiClient>().GetDevelopmentTokenAsync("CR-1042"));

        Assert.StartsWith("The server's answer could not be read.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_connection_is_a_friendly_unavailable_error()
    {
        _server.Respond = _ => throw new HttpRequestException("No such host is known. (api.apexpost.example:443)");
        using var host = Host("--ApexPostApi:Resilience:RetryBaseDelay=00:00:00.001");

        var exception = await UploadFailsAsync<ApiUnavailableException>(host);

        Assert.Equal(UnavailableReason.NoConnection, exception.Reason);
        Assert.Equal("The server cannot be reached: check the network. Your scans are saved on this device and will be sent on the next sync.", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task Cancelling_the_call_is_not_reported_as_a_server_problem()
    {
        _server.RespondAsync = async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return StubServer.Json(HttpStatusCode.OK, "{}");
        };
        using var host = Host();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001", [Scan()], cancel.Token));
    }

    private void Answer(HttpStatusCode status, string json) =>
        _server.Respond = _ => StubServer.Json(status, json, (int)status >= 400 ? "application/problem+json" : "application/json");

    private IHost Host(params string[] settings) => _app.CreateHost(TextReader.Null, TextWriter.Null, settings, _server.Install);

    private async Task<T> UploadFailsAsync<T>(IHost? host = null) where T : Exception
    {
        using var own = host is null ? Host() : null;
        return await Assert.ThrowsAsync<T>(() => Upload(host ?? own!));
    }

    private static Task<ScanUploadResult> Upload(IHost host) =>
        host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001", [Scan()]);

    private static ShipmentScan Scan() => ShipmentScan.Record("APX-30000001", ScanType.Pickup, "CR-1042", "Riverside Depot", DateTimeOffset.UtcNow.AddMinutes(-5));
}
