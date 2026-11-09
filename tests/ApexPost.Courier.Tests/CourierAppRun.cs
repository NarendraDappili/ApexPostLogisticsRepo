using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Tests.Api;
using ApexPost.Courier.Screens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Courier.Tests;

/// <summary>
/// The whole app, built by <see cref="CourierAppHost"/> exactly as Program.cs builds it, on a database in its own
/// temporary folder. A test either runs a scripted session or reads the services the session used.
/// </summary>
internal sealed class CourierAppRun : IDisposable
{
    public CourierAppRun()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "apexpost-courier-app-tests", Guid.NewGuid().ToString("N"));
    }

    public string DataDirectory { get; }

    /// <summary>Everything the app wrote, menus and log lines together, as a courier would see it.</summary>
    public string Output { get; private set; } = string.Empty;

    public int ExitCode { get; private set; }

    /// <summary>The API the app talks to: an in-memory fake, so no test ever reaches a real server.</summary>
    public FakeApi Api { get; } = new();

    /// <summary>
    /// Builds a host on this run's folder; the caller disposes it. The app talks to <see cref="Api"/> unless
    /// <paramref name="configureServices"/> installs another stub, and retries wait milliseconds, not seconds.
    /// </summary>
    public IHost CreateHost(TextReader input, TextWriter output, string[]? settings = null, Action<IServiceCollection>? configureServices = null) =>
        CourierAppHost.Create(
            [$"--Courier:DataDirectory={DataDirectory}", "--ApexPostApi:Resilience:RetryBaseDelay=00:00:00.005", .. settings ?? []],
            input,
            output,
            services =>
            {
                Api.Server.Install(services);
                configureServices?.Invoke(services);
            });

    /// <summary>Runs one session with the given answers, then gives <paramref name="inspect"/> the session's services.</summary>
    public async Task<string> RunAsync(string[] answers, Action<IServiceProvider>? inspect = null)
    {
        var output = new StringWriter();
        using (var host = CreateHost(new StringReader(string.Join(Environment.NewLine, answers)), output))
        {
            ExitCode = await host.Services.GetRequiredService<CourierApp>().RunAsync();
            inspect?.Invoke(host.Services);
        }

        Output = output.ToString();
        return Output;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder (or none, if the test never opened the database) must not fail the test.
        }
    }

    public static IReadOnlyList<ShipmentScan> Scans(IServiceProvider services) => services.GetRequiredService<IScanRepository>().ListAll();
}
