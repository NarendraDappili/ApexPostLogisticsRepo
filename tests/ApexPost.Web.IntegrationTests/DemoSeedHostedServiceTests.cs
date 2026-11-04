using ApexPost.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace ApexPost.Web.IntegrationTests;

/// <summary>
/// The start-up seeding guard: synthetic demo data is written only in Development, only when enabled, and only with a
/// password from user-secrets. These cases never reach the database: no scope is even created.
/// </summary>
public sealed class DemoSeedHostedServiceTests
{
    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Staging", "true")]
    [InlineData("Development", "false")]
    [InlineData("Development", null)]
    public async Task Seeding_is_skipped_outside_development_or_when_it_is_not_enabled(string environment, string? enabled)
    {
        var scopes = new RefusingScopeFactory();
        var logger = new CapturingLogger();

        await Service(environment, enabled, password: "a-long-enough-password", scopes, logger).StartAsync(CancellationToken.None);

        Assert.Equal(0, scopes.Requests);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Seeding_without_a_configured_password_is_refused_with_a_warning_that_says_how_to_set_one(string? password)
    {
        var scopes = new RefusingScopeFactory();
        var logger = new CapturingLogger();

        await Service("Development", "true", password, scopes, logger).StartAsync(CancellationToken.None);

        Assert.Equal(0, scopes.Requests);
        var (level, message) = Assert.Single(logger.Messages);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("ApexPost:DemoSeed:Password is not configured", message, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stopping_the_seeder_does_nothing()
    {
        await Service("Development", "true", null, new RefusingScopeFactory(), new CapturingLogger()).StopAsync(CancellationToken.None);
    }

    private static DemoDataSeedHostedService Service(string environment, string? enabled, string? password, IServiceScopeFactory scopes, ILogger<DemoDataSeedHostedService> logger)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApexPost:DemoSeed:Enabled"] = enabled,
            ["ApexPost:DemoSeed:Password"] = password,
        }).Build();
        return new DemoDataSeedHostedService(scopes, configuration, new HostingEnvironment { EnvironmentName = environment }, logger);
    }

    private sealed class RefusingScopeFactory : IServiceScopeFactory
    {
        public int Requests { get; private set; }

        public IServiceScope CreateScope()
        {
            Requests++;
            throw new InvalidOperationException("The guard should have returned before opening a scope.");
        }
    }

    private sealed class CapturingLogger : ILogger<DemoDataSeedHostedService>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add((logLevel, formatter(state, exception)));
    }
}
