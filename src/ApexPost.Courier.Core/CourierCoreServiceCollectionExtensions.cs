using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier;

/// <summary>
/// Registers the courier app's logic, the part every app head shares (ADR 0032).
/// </summary>
public static class CourierCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the typed v2 API client with its token handler and resilience pipeline, the uploader, the outbox,
    /// automatic sync and the sign-in session. Settings are read from the <c>ApexPostApi</c> and
    /// <c>Courier:AutoSync</c> sections.
    /// </summary>
    /// <remarks>
    /// The app head registers what depends on the device:
    /// <list type="bullet">
    /// <item>a <see cref="TimeProvider"/>;</item>
    /// <item>the offline store (<see cref="CourierDatabase"/>, <see cref="ScanChangeMonitor"/>, <see cref="IScanRepository"/>), whose folder differs per platform;</item>
    /// <item>an <see cref="ITokenStore"/> (memory in the console, SecureStorage on a phone);</item>
    /// <item>an <see cref="INetworkStatus"/> (the operating system in the console, Connectivity on a phone).</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddCourierCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The outbox uploads to the v2 API, in batches of 20, and marks each scan from the API's answer. AutoSync starts
        // it when the network comes back and every minute while online.
        services.AddSingleton<IShipmentUploader, ApiShipmentUploader>();
        services.Configure<AutoSyncOptions>(configuration.GetSection(AutoSyncOptions.Section));
        services.AddSingleton<AutoSyncService>();
        services.AddSingleton(provider => new OutboxService(
            provider.GetRequiredService<IScanRepository>(),
            provider.GetRequiredService<IShipmentUploader>(),
            new OutboxOptions(),
            provider.GetRequiredService<ILogger<OutboxService>>()));
        services.AddSingleton<SyncHistory>();
        services.AddSingleton<SyncService>();

        // The v2 API: a typed client from IHttpClientFactory, its address from configuration, the courier's token added
        // by AuthHeaderHandler from the token store.
        services.AddOptions<ApexApiOptions>().Bind(configuration.GetSection(ApexApiOptions.Section));
        services.AddSingleton<IValidateOptions<ApexApiOptions>, ApexApiOptionsValidator>();
        services.AddTransient<AuthHeaderHandler>();
        // Retry, circuit breaker and timeouts (RESILIENCE.md). The resilience handler comes first, so every retry goes
        // through AuthHeaderHandler again and picks up the current token.
        services.Configure<ApiResilienceOptions>(configuration.GetSection(ApiResilienceOptions.Section));
        services.AddSingleton<ServerAvailability>();
        services.AddHttpClient<ApexApiClient>((provider, client) => client.BaseAddress = provider.GetRequiredService<IOptions<ApexApiOptions>>().Value.Root)
            .AddApexApiResilience()
            .AddHttpMessageHandler<AuthHeaderHandler>();

        services.AddSingleton<CourierSession>();
        return services;
    }
}
