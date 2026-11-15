using System.Net;
using System.Text;

namespace ApexPost.UnitTests.Courier.Api;

/// <summary>
/// Stands in for the network under an <see cref="HttpClient"/>: no socket is ever opened (TESTING-STRATEGY.md §7.4).
/// It records every request with its body, and answers with whatever the test gives it, including a delay or an
/// exception.
/// <para>
/// A hand-written fake is used rather than <c>Moq.Protected()</c>. It reads like the answers it gives, and it has no
/// string-named set-up of the protected <c>SendAsync</c> that would break silently on a rename.
/// </para>
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri? Uri, string? Body)> Requests { get; } = [];

    public static FakeHttpMessageHandler Answering(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri, body));
        return await answer(request, cancellationToken);
    }
}
