using System.Net;
using ApexPost.Client;

namespace ApexPost.Api.Tests.Client;

/// <summary>D5 in the client: a bearer token goes over HTTPS, or over plain HTTP only to this machine.</summary>
public sealed class BearerTokenHandlerTests
{
    [Theory]
    [InlineData("https://api.apexpost.example/api/v2/shipments")]
    [InlineData("http://localhost:5245/api/v2/shipments")]
    [InlineData("http://127.0.0.1:5245/api/v2/shipments")]
    [InlineData("http://[::1]:5245/api/v2/shipments")]
    public async Task The_token_is_sent_over_https_or_to_this_machine(string url)
    {
        var (handler, inner) = Handler();
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer secret-token", inner.Authorization);
    }

    [Theory]
    [InlineData("http://api.apexpost.example/api/v2/shipments")]
    [InlineData("http://192.168.1.20:5245/api/v2/shipments")]
    public async Task A_token_is_never_sent_over_plain_http_to_another_host(string url)
    {
        var asked = false;
        var (handler, inner) = Handler(() => asked = true);
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(url));

        Assert.StartsWith("Refusing to send a bearer token to http://", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", exception.Message, StringComparison.Ordinal);
        Assert.False(asked);
        Assert.Null(inner.Authorization);
    }

    private static (BearerTokenHandler Handler, Recorder Inner) Handler(Action? onAsked = null)
    {
        var inner = new Recorder();
        var handler = new BearerTokenHandler(_ =>
        {
            onAsked?.Invoke();
            return ValueTask.FromResult<string?>("secret-token");
        })
        { InnerHandler = inner };
        return (handler, inner);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
