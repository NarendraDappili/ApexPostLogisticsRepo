using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// Lets dotnet-ef build the model for "migrations add" and "migrations script" without starting the web app.
/// Those commands never connect. For "database update", set APEXPOST_DB_CONNECTION; the fallback is a
/// Windows-authentication connection to the local default instance, so no credential is ever stored here.
/// </summary>
public sealed class ApexPostDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApexPostDbContext>
{
    public const string ConnectionVariable = "APEXPOST_DB_CONNECTION";

    public ApexPostDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApexPostDbContext).Assembly.GetName().Name))
            .Options;
        return new ApexPostDbContext(options);
    }
}
