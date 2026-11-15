using System.Net.Http.Headers;
using System.Security.Cryptography;
using ApexPost.Api.Security;
using ApexPost.Infrastructure.Persistence;
using ApexPost.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Specs.Support;

/// <summary>
/// Head office: the real tracking API (<c>ApexPost.Api</c>) hosted in memory by <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// It runs over a throwaway SQL Server database built from the real migrations and dropped afterwards, and has its
/// own signing key, so a developer's keys and data are never used. One instance serves one feature: the hooks start
/// it before the feature and dispose it after (<see cref="Hooks"/>).
/// </summary>
public sealed class HeadOffice : WebApplicationFactory<Program>
{
    private const string Issuer = "apexpost-specs";
    private const string Audience = "apexpost-api-specs";

    // Fixed instants, so whether a token is accepted never depends on when the specs run (TESTING-STRATEGY.md §8).
    private static readonly DateTime ValidFrom = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ValidUntil = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);
    private int _lastTrackingNumber = 51_000_000;

    public string DatabaseName { get; } = $"ApexPostDb_SpecTest_{Guid.NewGuid():N}";

    /// <summary>A tracking number not yet used in this feature's database.</summary>
    public string NextTrackingNumber() => $"APX-{Interlocked.Increment(ref _lastTrackingNumber):D8}";

    /// <summary>A client signed in with these roles, as head office staff or a courier would be.</summary>
    public HttpClient ClientFor(string[] roles, string? courierId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(roles, courierId));
        return client;
    }

    /// <summary>A bearer token this API accepts, for the given roles and courier.</summary>
    public string TokenFor(string[] roles, string? courierId = null)
    {
        var claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = courierId ?? "head-office", ["role"] = roles };
        if (courierId is not null) claims[ApiRoles.CourierIdClaim] = courierId;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = ValidFrom,
            NotBefore = ValidFrom,
            Expires = ValidUntil,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Starts the API, which migrates the database first.</summary>
    public void Start() => _ = Services;

    public async Task StopAsync()
    {
        await using (var db = new ApexPostDbContext(new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer(TestSqlServer.ForDatabase(DatabaseName)).Options))
        {
            await db.Database.EnsureDeletedAsync();
        }

        await DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Program.cs reads these before Build(), which only UseSetting reaches.
        builder.UseSetting("ConnectionStrings:ApexPostDb", TestSqlServer.ForDatabase(DatabaseName));
        builder.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", Issuer);
        builder.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", Audience);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", Issuer);
        builder.UseSetting("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(_signingKey));
        builder.UseSetting("PartnerApi:Keys:0:Partner", "Specs Partner");
        builder.UseSetting("PartnerApi:Keys:0:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        // Registered first, so the schema exists before the API serves anything.
        builder.ConfigureServices(services => services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(provider =>
            new Migrator(provider.GetRequiredService<IServiceScopeFactory>(), DatabaseName))));
    }

    private sealed class Migrator(IServiceScopeFactory scopes, string expectedDatabase) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            // Development hosts also load user-secrets; refuse to touch anything but this throwaway database.
            var actual = db.Database.GetDbConnection().Database;
            if (!string.Equals(actual, expectedDatabase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The specs refused to run against database '{actual}'; expected the throwaway '{expectedDatabase}'.");
            }

            await db.Database.MigrateAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
