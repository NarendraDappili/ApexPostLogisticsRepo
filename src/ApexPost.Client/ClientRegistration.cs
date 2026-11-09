using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Client;

/// <summary>Registers the generated client with the HttpClient factory.</summary>
public static class ClientRegistration
{
    /// <summary>
    /// Registers <see cref="IApexPostClient"/> as a typed HttpClient (AddHttpClient) whose requests go to
    /// <paramref name="baseAddress"/>, for example http://localhost:5245/. The factory owns and recycles the
    /// HttpClient, so the client never disposes it. Returns the builder, to add handlers such as retries.
    /// </summary>
    public static IHttpClientBuilder AddApexPostClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        // The generated client sends relative URLs (api/v2/shipments); they resolve below the base address only when
        // it ends with a slash.
        var root = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
        return services.AddHttpClient<IApexPostClient, ApexPostClient>(client => client.BaseAddress = root);
    }

    /// <summary>
    /// Sends a bearer token with every request: the API requires one with the Courier or Admin role. The function is
    /// called per request, so it can read a token that is renewed while the program runs.
    /// </summary>
    public static IHttpClientBuilder WithBearerToken(this IHttpClientBuilder builder, Func<IServiceProvider, CancellationToken, ValueTask<string?>> getToken)
    {
        ArgumentNullException.ThrowIfNull(getToken);
        return builder.AddHttpMessageHandler(services => new BearerTokenHandler(cancellationToken => getToken(services, cancellationToken)));
    }
}
