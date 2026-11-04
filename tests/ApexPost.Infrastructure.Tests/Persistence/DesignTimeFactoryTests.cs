using ApexPost.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Persistence;

/// <summary>
/// The factory dotnet-ef uses (CI's "has-pending-model-changes" check, "migrations script", "database update"). No
/// connection is opened.
/// </summary>
public sealed class DesignTimeFactoryTests
{
    [Fact]
    public void The_connection_string_comes_from_the_environment_and_every_migration_is_found()
    {
        const string variable = "ConnectionStrings__ApexPostDb";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "Server=design-time-test;Database=ApexPostDb_DesignTime;Trusted_Connection=True");
        try
        {
            using var db = new ApexPostDesignTimeDbContextFactory().CreateDbContext([]);

            Assert.Equal("ApexPostDb_DesignTime", new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog);
            var migrations = db.Database.GetMigrations().ToArray();
            Assert.Equal("20261030172857_InitialCreate", migrations[0]);
            Assert.EndsWith("_AddAuditTrail", migrations[^1], StringComparison.Ordinal);
            Assert.Equal(migrations.Order(StringComparer.Ordinal), migrations);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
