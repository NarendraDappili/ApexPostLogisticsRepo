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
        TestTokens.Configure(builder);
        TestPartnerKeys.Configure(builder);
        builder.UseEnvironment("Development");
    }

    /// <summary>Every client sends a valid Courier and Admin token; the authentication tests remove or replace it.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = TestTokens.DefaultHeader();
    }
}
