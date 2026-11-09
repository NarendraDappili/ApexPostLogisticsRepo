using System.Net;
using System.Text;
using System.Text.Json;
using ApexPost.Api.Tests.Hosting;
using ApexPost.Application.Tracking;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// B5: every error is ProblemDetails, and an unhandled exception never shows its message or stack trace outside
/// Development. The hosts need no database: the query service is replaced by one that fails the way a real
/// dependency would, with a message that must not leak.
/// </summary>
public sealed class ErrorHandlingTests
{
    private const string SecretMessage = "Login failed for user 'apexpost_api' on server sql-prod-01.";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_a_500_says_only_that_an_error_happened(string environment)
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = Host(environment, new InvalidOperationException(SecretMessage), logs);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/shipments");
        var text = await response.Content.ReadAsStringAsync();
        var problem = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(("An unexpected server error occurred.", "The error has been logged. Quote the traceId when you report it.", "GET /api/shipments"),
            (problem.GetProperty("title").GetString(), problem.GetProperty("detail").GetString(), problem.GetProperty("instance").GetString()));
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.DoesNotContain("sql-prod-01", text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.DoesNotContain(" at ", text);
        // The details are not lost: they are in the server log, with the stack trace.
        var logged = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("ApexPost.Api.Errors.ApiExceptionHandler", logged.Category);
        Assert.Equal(SecretMessage, logged.Exception!.Message);
    }

    [Fact]
    public async Task In_development_a_500_carries_the_exception_for_the_developer()
    {
        await using var factory = Host("Development", new InvalidOperationException(SecretMessage));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/shipments");
        var exception = (await response.JsonAsync()).GetProperty("exception");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(("System.InvalidOperationException", SecretMessage), (exception.GetProperty("type").GetString(), exception.GetProperty("message").GetString()));
        Assert.Contains(nameof(FailingQueries), exception.GetProperty("stackTrace").GetString());
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentOutOfRangeException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(TimeoutException))]
    public async Task Only_the_applications_typed_errors_are_client_errors(Type exceptionType)
    {
        // An ArgumentException from a library is a bug or an outage, not a bad request: by type alone it is a 500.
        var failure = (Exception)Activator.CreateInstance(exceptionType, SecretMessage)!;
        await using var factory = Host("Production", failure);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/shipments");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("sql-prod-01", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Outside_development_a_business_error_keeps_its_message()
    {
        await using var factory = Host("Production", new InvalidOperationException(SecretMessage));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/shipments/APX-24000001");
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(("No shipment has the tracking number APX-24000001.", "GET /api/shipments/APX-24000001"),
            (problem.GetProperty("detail").GetString(), problem.GetProperty("instance").GetString()));
    }

    [Theory]
    [InlineData("GET", "/api/no-such-route", null, HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/shipments/APX-123", null, HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/api/shipments", null, HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/api/shipments", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("POST", "/api/shipments", "application/json", HttpStatusCode.BadRequest)]
    public async Task Errors_the_framework_answers_are_problem_details_too(string method, string path, string? contentType, HttpStatusCode expected)
    {
        await using var factory = Host("Production", new InvalidOperationException(SecretMessage));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (contentType is not null) request.Content = new StringContent("{}", Encoding.UTF8, contentType);

        using var response = await client.SendAsync(request);
        var problem = await response.JsonAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("title", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task A_successful_request_is_untouched()
    {
        await using var factory = Host("Production", new InvalidOperationException(SecretMessage));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    private static WebApplicationFactory<Program> Host(string environment, Exception failure, CapturingLoggerProvider? logs = null) =>
        new OfflineApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services => services.AddScoped<ITrackingQueries>(_ => new FailingQueries(failure)));
            if (logs is not null) builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    /// <summary>Lists fail the way a broken dependency would; a single shipment is simply not found.</summary>
    private sealed class FailingQueries(Exception failure) : ITrackingQueries
    {
        public Task<PagedResult<TrackedShipmentView>> ListAsync(TrackingListQuery query, CancellationToken cancellationToken = default) => throw failure;

        public Task<TrackedShipmentView?> GetAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) => Task.FromResult<TrackedShipmentView?>(null);

        public Task<IReadOnlyList<TrackingScanView>?> GetScanHistoryAsync(TrackingNumber trackingNumber, CancellationToken cancellationToken = default) => throw failure;
    }
}
