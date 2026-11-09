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
}
