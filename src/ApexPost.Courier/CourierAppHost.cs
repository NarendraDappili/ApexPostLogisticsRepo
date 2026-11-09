using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Courier.Screens;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier;

/// <summary>
/// Builds the app's host: configuration, logging and the services the screens use. The screens read and write
/// through the given reader and writer, so tests run the whole app with scripted input.
/// </summary>
internal static class CourierAppHost
{
    /// <param name="configureServices">For tests: runs after the app's own registrations, for example to replace the network.</param>
    public static IHost Create(string[] args, TextReader input, TextWriter output, Action<IServiceCollection>? configureServices = null)
    {
        // appsettings.json sits next to the program; read it from there whatever directory the program is started from.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

        // Log lines go to the screen as they happen, between the menu lines, not later from a queue.
        var screenOutput = TextWriter.Synchronized(output);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LineLoggerProvider(screenOutput));

        builder.Services.Configure<CourierAppOptions>(builder.Configuration.GetSection(CourierAppOptions.Section));
        builder.Services.AddSingleton(TimeProvider.System);

        // The device's offline store from Part A. The monitor is created after the database, so it is disposed first.
        builder.Services.AddSingleton(services => new CourierDatabase(
            new CourierDatabaseOptions { Directory = services.GetRequiredService<IOptions<CourierAppOptions>>().Value.ResolveDataDirectory() },
            services.GetRequiredService<ILogger<CourierDatabase>>()));
        builder.Services.AddSingleton(services => new ScanChangeMonitor(
            services.GetRequiredService<CourierDatabase>(), services.GetRequiredService<ILogger<ScanChangeMonitor>>()));
        builder.Services.AddSingleton<IScanRepository>(services => new CouchbaseScanRepository(
            services.GetRequiredService<CourierDatabase>(), services.GetRequiredService<ILogger<CouchbaseScanRepository>>()));

        // Uploads still go to the Part A stand-in server; nothing here uses the network yet.
        builder.Services.AddSingleton<IShipmentUploader>(services => new FakeShipmentUploader(services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(services => new OutboxService(
            services.GetRequiredService<IScanRepository>(),
            services.GetRequiredService<IShipmentUploader>(),
            new OutboxOptions(),
            services.GetRequiredService<ILogger<OutboxService>>()));
        builder.Services.AddSingleton<SyncHistory>();
        builder.Services.AddSingleton<SyncService>();

        // The v2 API: a typed client from IHttpClientFactory, its address from configuration, the courier's token added
        // by AuthHeaderHandler from the token store.
        builder.Services.AddOptions<ApexApiOptions>().Bind(builder.Configuration.GetSection(ApexApiOptions.Section));
        builder.Services.AddSingleton<IValidateOptions<ApexApiOptions>, ApexApiOptionsValidator>();
        builder.Services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.Services.AddHttpClient<ApexApiClient>((services, client) => client.BaseAddress = services.GetRequiredService<IOptions<ApexApiOptions>>().Value.Root)
            .AddHttpMessageHandler<AuthHeaderHandler>();

        builder.Services.AddSingleton<CourierSession>();
        builder.Services.AddSingleton(services => new ConsoleScreen(input, screenOutput, services.GetRequiredService<ScanChangeMonitor>()));
        builder.Services.AddSingleton<LoginScreen>();
        builder.Services.AddSingleton<NewScanScreen>();
        builder.Services.AddSingleton<PendingScansScreen>();
        builder.Services.AddSingleton<SyncStatusScreen>();
        builder.Services.AddSingleton<CourierApp>();
        configureServices?.Invoke(builder.Services);

        var host = builder.Build();
        // Check the API address now, so a wrong one stops the start with its reason, not the first sync.
        _ = host.Services.GetRequiredService<IOptions<ApexApiOptions>>().Value;
        return host;
    }
}
