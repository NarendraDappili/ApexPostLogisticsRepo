using System.Net;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// D7: the API client's resilience pipeline, on the app's real composition with a stub network. Retries happen only
/// on network errors, timeouts, 408 and 5xx, and only for requests safe to send twice; the circuit opens after
/// repeated failures, shows the offline banner, fails calls at once while open, and closes when the server answers
/// again. Every retry, timeout and circuit change is logged. The delays are shortened for the tests.
/// </summary>
public sealed class ResilienceTests : IDisposable
{
    private static readonly string[] FastSettings =
    [
        "--ApexPostApi:Resilience:RetryBaseDelay=00:00:00.010",
        "--ApexPostApi:Resilience:AttemptTimeout=00:00:00.300",
        "--ApexPostApi:Resilience:TotalTimeout=00:00:03",
        "--ApexPostApi:Resilience:BreakDuration=00:00:00.500",
    ];

    private readonly CourierAppRun _app = new();
    private readonly StubServer _server = new();
    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _app.Dispose();
        _server.Dispose();
    }

    [Fact]
    public void The_defaults_are_the_values_resilience_md_explains()
    {
        var defaults = new ApiResilienceOptions();

        Assert.Equal(
            (3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), 0.5, 4, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)),
            (defaults.MaxRetryAttempts, defaults.RetryBaseDelay, defaults.AttemptTimeout, defaults.TotalTimeout, defaults.BreakerFailureRatio,
                defaults.BreakerMinimumThroughput, defaults.BreakerSamplingDuration, defaults.BreakDuration));
    }

    [Fact]
    public async Task A_scan_upload_is_retried_after_5xx_and_sends_the_same_scans_and_token_each_time()
    {
        _server.RespondInTurn(() => Status(HttpStatusCode.ServiceUnavailable), () => Status(HttpStatusCode.BadGateway), Accepted);
        using var host = Host();
        await host.Services.GetRequiredService<ITokenStore>().SaveAsync("courier-token");

        var result = await Upload(host);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(3, _server.Requests.Count);
        Assert.Single(_server.Requests.Select(request => request.Body).Distinct());
        Assert.All(_server.Requests, request => Assert.Equal("Bearer courier-token", request.Authorization));
        Assert.Contains("Retry 1 of 3 for POST /api/v2/shipments/APX-30000001/scans", Log, StringComparison.Ordinal);
        Assert.Contains("after 503 ServiceUnavailable", Log, StringComparison.Ordinal);
        Assert.Contains("Retry 2 of 3", Log, StringComparison.Ordinal);
        Assert.DoesNotContain("courier-token", Log, StringComparison.Ordinal);
        // Polly's own telemetry would repeat each line; the app logs the events itself.
        Assert.DoesNotContain("Polly", Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_lasting_408_or_5xx_is_tried_four_times_in_all(HttpStatusCode status)
    {
        _server.Respond = _ => Status(status);
        using var host = Host();

        var exception = await Assert.ThrowsAsync<ApexApiException>(() => Upload(host));

        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(4, _server.Requests.Count);
        Assert.Contains("Retry 3 of 3", Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_4xx_other_than_408_is_never_retried(HttpStatusCode status)
    {
        _server.Respond = _ => Status(status);
        using var host = Host();

        var exception = await Assert.ThrowsAsync<ApexApiException>(() => Upload(host));

        Assert.Equal(status, exception.StatusCode);
        Assert.Single(_server.Requests);
        Assert.DoesNotContain("Retry", Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_network_error_is_retried()
    {
        var calls = 0;
        _server.Respond = _ => ++calls < 3 ? throw new HttpRequestException("No route to host") : Accepted();
        using var host = Host();

        await Upload(host);

        Assert.Equal(3, _server.Requests.Count);
        Assert.Contains("after HttpRequestException", Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_post_that_is_not_marked_safe_is_never_retried()
    {
        _server.Respond = _ => Status(HttpStatusCode.ServiceUnavailable);
        using var host = Host();

        await Assert.ThrowsAsync<ApexApiException>(() => host.Services.GetRequiredService<ApexApiClient>().GetDevelopmentTokenAsync("CR-1042"));

        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task A_slow_attempt_is_abandoned_and_tried_again()
    {
        var calls = 0;
        _server.RespondAsync = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Accepted();
        };
        using var host = Host();

        await Upload(host);

        Assert.Equal(2, _server.Requests.Count);
        Assert.Contains("An attempt was abandoned after 00:00:00.3", Log, StringComparison.Ordinal);
        Assert.Contains("after a timeout", Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_whole_call_is_abandoned_after_the_total_timeout()
    {
        _server.RespondAsync = async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Accepted();
        };
        using var host = Host("--ApexPostApi:Resilience:AttemptTimeout=00:00:00.400", "--ApexPostApi:Resilience:TotalTimeout=00:00:01");
        var started = TimeProvider.System.GetTimestamp();

        var exception = await Assert.ThrowsAsync<ApiUnavailableException>(() => Upload(host));

        Assert.Equal(UnavailableReason.TimedOut, exception.Reason);
        Assert.IsType<TimeoutRejectedException>(exception.InnerException);
        Assert.InRange(TimeProvider.System.GetElapsedTime(started), TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(2.5));
        // 400 ms tries from 0, 0.41 and 0.83 s: the third is cut short by the 1 s total, and no fourth starts.
        Assert.InRange(_server.Requests.Count, 2, 3);
        Assert.Contains("An attempt was abandoned after 00:00:00.4", Log, StringComparison.Ordinal);
        Assert.Contains("The API call was abandoned after 00:00:01 in all", Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_failures_open_the_circuit_the_app_shows_the_banner_and_calls_fail_at_once()
    {
        _server.Respond = _ => Status(HttpStatusCode.ServiceUnavailable);
        using var host = HostWithInput("CR-001\n\n4\n6");
        var server = host.Services.GetRequiredService<ServerAvailability>();

        await Assert.ThrowsAsync<ApexApiException>(() => Upload(host));
        var sentBeforeOpen = _server.Requests.Count;
        var refused = await Assert.ThrowsAsync<ApiUnavailableException>(() => Upload(host));
        await host.Services.GetRequiredService<CourierApp>().RunAsync();

        Assert.Equal(ConnectionState.Open, server.State);
        Assert.Equal((UnavailableReason.CircuitOpen, typeof(BrokenCircuitException)), (refused.Reason, refused.InnerException!.GetType()));
        Assert.Equal(4, sentBeforeOpen);
        Assert.Equal(sentBeforeOpen, _server.Requests.Count);
        Assert.Contains("Circuit opened for 0.5 s after 503 ServiceUnavailable: working offline", Log, StringComparison.Ordinal);
        Assert.Contains("*** Server unavailable – working offline ***", Log, StringComparison.Ordinal);
        Assert.Contains("Connection (circuit): open since", Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_circuit_closes_and_the_banner_goes_when_the_server_answers_again()
    {
        _server.Respond = _ => Status(HttpStatusCode.ServiceUnavailable);
        using var host = HostWithInput("CR-001\n\n6");
        var server = host.Services.GetRequiredService<ServerAvailability>();
        await Assert.ThrowsAsync<ApexApiException>(() => Upload(host));
        Assert.Equal(ConnectionState.Open, server.State);

        _server.Respond = _ => Accepted();
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        await Upload(host);
        await host.Services.GetRequiredService<CourierApp>().RunAsync();

        Assert.Equal(ConnectionState.Closed, server.State);
        Assert.Contains("Circuit half-open: the next call tries the server again", Log, StringComparison.Ordinal);
        Assert.Contains("Circuit closed: the server answers again", Log, StringComparison.Ordinal);
        Assert.DoesNotContain("***", Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", false, true)]
    [InlineData("PUT", false, true)]
    [InlineData("DELETE", false, true)]
    [InlineData("POST", false, false)]
    [InlineData("PATCH", false, false)]
    [InlineData("POST", true, true)]
    public void Only_idempotent_requests_or_those_marked_safe_are_retried(string method, bool marked, bool retried)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://localhost:7245/api/v2/shipments");
        if (marked) request.Options.Set(ApiResilience.SafeToRetry, true);

        Assert.Equal(retried, ApiResilience.IsSafeToRetry(request));
    }

    private string Log => _output.ToString();

    private IHost Host(params string[] settings) => HostWithInput(string.Empty, settings);

    private IHost HostWithInput(string input, params string[] settings) =>
        _app.CreateHost(new StringReader(input), _output, [.. FastSettings, .. settings], _server.Install);

    private static Task<ScanUploadResult> Upload(IHost host) =>
        host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001",
            [ShipmentScan.Record("APX-30000001", ScanType.Pickup, "CR-001", "Riverside Depot", DateTimeOffset.UtcNow.AddMinutes(-5))]);

    private static HttpResponseMessage Status(HttpStatusCode status) =>
        StubServer.Json(status, $$"""{ "title": "{{status}}", "status": {{(int)status}} }""", "application/problem+json");

    private static HttpResponseMessage Accepted() =>
        StubServer.Json(HttpStatusCode.OK, """{ "accepted": 1, "duplicatesSkipped": 0, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" }""");
}
