using ApexPost.TestSupport;
using ApexPost.Application.Abstractions;
using ApexPost.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.IntegrationTests.Sql;

/// <summary>
/// A disposable database built from the real migrations and dropped afterwards; the development database is
/// never used. The server is found by <see cref="TestSqlServer"/>.
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    public SqlServerTestDatabase()
    {
        ConnectionString = TestSqlServer.ForDatabase($"ApexPostDb_Test_{Guid.NewGuid():N}");
    }

    public string ConnectionString { get; }

    /// <summary>A new context, as one web request would get, with the production audit-column interceptor.</summary>
    public ApexPostDbContext CreateContext(ICurrentStaffContext? currentStaff = null, int? maxBatchSize = null, params IInterceptor[] interceptors) =>
        new(CreateOptions(currentStaff, maxBatchSize, interceptors));

    /// <summary>The options one request's contexts share, as the web app's dependency injection builds them.</summary>
    public DbContextOptions<ApexPostDbContext> CreateOptions(ICurrentStaffContext? currentStaff = null, int? maxBatchSize = null, params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(ConnectionString, sql => { if (maxBatchSize is { } size) sql.MaxBatchSize(size); })
            .AddInterceptors([new AuditColumnsInterceptor(TimeProvider.System, currentStaff), .. interceptors])
            .Options;

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
