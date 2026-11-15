using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
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

        // Log lines go to the screen as they happen, between the menu lines, not later from a queue. One shared output
        // knows when a prompt is waiting, so a background log line never ends up on the prompt's line.
        var screenOutput = new ScreenOutput(output);
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

        // The courier logic every app head shares: the v2 API client with its token handler and resilience pipeline, the
        // uploader, the outbox, automatic sync and the session (ApexPost.Courier.Core, ADR 0032).
        builder.Services.AddCourierCore(builder.Configuration);

        // What this head supplies for the device: the token lives in memory only (a phone would use SecureStorage), and
        // the network comes from the operating system (a phone would use Connectivity).
        builder.Services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        builder.Services.AddSingleton<INetworkStatus, SystemNetworkStatus>();

        builder.Services.AddSingleton(services => new ConsoleScreen(input, screenOutput, services.GetRequiredService<ScanChangeMonitor>()));
        builder.Services.AddSingleton<LoginScreen>();
        builder.Services.AddSingleton<NewScanScreen>();
        builder.Services.AddSingleton<PendingScansScreen>();
        builder.Services.AddSingleton<SyncStatusScreen>();
        builder.Services.AddSingleton<OutForDeliveryScreen>();
        builder.Services.AddSingleton<CourierApp>();
        configureServices?.Invoke(builder.Services);

        var host = builder.Build();
        // Check the API address now, so a wrong one stops the start with its reason, not the first sync.
        _ = host.Services.GetRequiredService<IOptions<ApexApiOptions>>().Value;
        return host;
    }
}
