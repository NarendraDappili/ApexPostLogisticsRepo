using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApexPost.Api.Tests.Hosting;

/// <summary>
/// The API host with a connection string that is never opened, for tests of the pipeline and the composition that
/// need no database. Setting it here also keeps a developer's user-secret (the real database) out of the tests.
/// </summary>
public sealed class OfflineApiFactory : WebApplicationFactory<Program>
{
    public const string UnusedConnectionString = "Server=(placeholder);Database=ApexPostDb_ApiTests_Unused;Trusted_Connection=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads the connection string before Build(), which only UseSetting reaches.
        builder.UseSetting("ConnectionStrings:ApexPostDb", UnusedConnectionString);
        builder.UseEnvironment("Development");
    }
}
