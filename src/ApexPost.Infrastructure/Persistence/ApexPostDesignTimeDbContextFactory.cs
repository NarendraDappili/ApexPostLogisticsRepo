using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// Lets dotnet-ef build the context without starting the web app. It reads the same
/// <c>ConnectionStrings:ApexPostDb</c> user-secret as ApexPost.Web (or the environment variable
/// <c>ConnectionStrings__ApexPostDb</c>), so "database update" targets the database the app uses.
/// "migrations add" and "migrations script" never connect, so they work without any secret.
/// </summary>
public sealed class ApexPostDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApexPostDbContext>
{
    public const string ConnectionStringName = "ApexPostDb";
    // The UserSecretsId of ApexPost.Web; both projects read the one secret.
    private const string WebUserSecretsId = "c64b2003-9f07-4be4-801b-323af9da40c8";
    // Only used when no secret is set, for commands that never open a connection.
    private const string OfflinePlaceholder = "Server=(placeholder);Database=ApexPostDb;Trusted_Connection=True";

    public ApexPostDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(WebUserSecretsId)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString(ConnectionStringName) ?? OfflinePlaceholder;
        var options = new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApexPostDbContext).Assembly.GetName().Name))
            .Options;
        return new ApexPostDbContext(options);
    }
}
