using ApexPost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Api.Tests.Hosting;

/// <summary>
/// The API host on a disposable SQL Server database, created from the migrations when the host starts and dropped
/// when the fixture is disposed. The development database is never used. The server comes from
/// APEXPOST_TEST_SQLSERVER, defaulting to the local default instance with Windows authentication.
/// One host serves a whole test collection; tests keep apart by using their own tracking numbers and cities.
/// </summary>
public sealed class SqlApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ServerVariable = "APEXPOST_TEST_SQLSERVER";
    private static int _nextNumber = 30_000_000;

    public SqlApiFactory()
    {
        DatabaseName = $"ApexPostDb_ApiTest_{Guid.NewGuid():N}";
        var server = Environment.GetEnvironmentVariable(ServerVariable) ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = DatabaseName }.ConnectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    /// <summary>A tracking number no other test in this run has used.</summary>
    public static string NextTrackingNumber() => $"APX-{Interlocked.Increment(ref _nextNumber):D8}";

    public Task InitializeAsync()
    {
        // Starts the host, which migrates the database.
        _ = Services;
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using (var db = new ApexPostDbContext(new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer(ConnectionString).Options))
        {
            await db.Database.EnsureDeletedAsync();
        }

        await DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Program.cs reads the connection string before Build(), which only UseSetting reaches.
        builder.UseSetting("ConnectionStrings:ApexPostDb", ConnectionString);
        // Registered first so the schema exists before the host serves anything.
        builder.ConfigureServices(services => services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(provider =>
            new Migrator(provider.GetRequiredService<IServiceScopeFactory>(), DatabaseName))));
    }

    private sealed class Migrator(IServiceScopeFactory scopes, string expectedDatabase) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            // Development hosts also load user-secrets; refuse to touch anything but this test database.
            var actual = db.Database.GetDbConnection().Database;
            if (!string.Equals(actual, expectedDatabase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Tests refused to run against database '{actual}'; expected the disposable '{expectedDatabase}'.");
            }

            await db.Database.MigrateAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFactory>
{
    public const string Name = "API on SQL Server";
}
