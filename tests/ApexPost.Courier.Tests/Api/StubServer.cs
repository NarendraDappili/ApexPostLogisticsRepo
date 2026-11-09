using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// Stands in for the network under the app's HttpClient pipeline: the app's own handlers (AuthHeaderHandler) still
/// run, and every request that reaches the wire is recorded with its body. The answer comes from <see cref="Respond"/>.
/// </summary>
internal sealed class StubServer : HttpMessageHandler
{
    private readonly ConcurrentQueue<Recorded> _requests = new();

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, "{}");

    public IReadOnlyList<Recorded> Requests => [.. _requests];

    /// <summary>Makes every HttpClient the app's factory creates send through this stub.</summary>
    public void Install(IServiceCollection services) => services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => this));

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new Recorded(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        return Respond(request);
    }

    // The factory disposes handlers it recycles; the stub outlives them for the test's assertions.
    protected override void Dispose(bool disposing)
    {
    }

    public sealed record Recorded(HttpMethod Method, Uri Uri, string? Authorization, string? Body);
}
