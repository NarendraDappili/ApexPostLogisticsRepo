using ApexPost.TestSupport;
using ApexPost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Web.IntegrationTests;

/// <summary>
/// A disposable SQL Server database for one test host: created from the migrations when the host starts and
/// dropped when the factory is disposed. The development database is never used.
/// The server is found by <see cref="TestSqlServer"/>.
/// </summary>
public sealed class TestDatabase
{
    public const string NamePrefix = "ApexPostDb_Test_";

    public TestDatabase()
    {
        Name = NamePrefix + Guid.NewGuid().ToString("N");
        ConnectionString = TestSqlServer.ForDatabase(Name);
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>Points the host at this database and migrates it before the host serves requests.</summary>
    public void Attach(IWebHostBuilder builder)
    {
        // Program.cs reads the connection string before Build(), which only UseSetting reaches.
        builder.UseSetting("ConnectionStrings:ApexPostDb", ConnectionString);
        // Registered first so the schema exists before any other hosted service runs.
        builder.ConfigureServices(services => services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(provider =>
            new Migrator(provider.GetRequiredService<IServiceScopeFactory>(), Name))));
    }

    public async Task DropAsync()
    {
        await using var db = new ApexPostDbContext(new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer(ConnectionString).Options);
        await db.Database.EnsureDeletedAsync();
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
