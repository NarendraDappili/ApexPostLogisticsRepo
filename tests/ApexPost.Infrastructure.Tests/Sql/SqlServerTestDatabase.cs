using ApexPost.Application.Abstractions;
using ApexPost.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// A disposable database built from the real migrations and dropped afterwards; the development database is
/// never used. The server comes from APEXPOST_TEST_SQLSERVER (default: local instance, Windows authentication).
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    private const string ServerVariable = "APEXPOST_TEST_SQLSERVER";

    public SqlServerTestDatabase()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable) ?? "Server=.;Trusted_Connection=True;TrustServerCertificate=True";
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = $"ApexPostDb_Test_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public string ConnectionString { get; }

    /// <summary>A new context, as one web request would get, with the production audit-column interceptor.</summary>
    public ApexPostDbContext CreateContext(ICurrentStaffContext? currentStaff = null, int? maxBatchSize = null, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(ConnectionString, sql => { if (maxBatchSize is { } size) sql.MaxBatchSize(size); })
            .AddInterceptors([new AuditColumnsInterceptor(TimeProvider.System, currentStaff), .. interceptors])
            .Options);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerTestDatabase>
{
    public const string Name = "SQL Server";
}
