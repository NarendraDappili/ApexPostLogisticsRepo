using ApexPost.Infrastructure.Seed;

namespace ApexPost.Web.Services;

/// <summary>
/// Seeds the synthetic demo data set before the server accepts requests, in Development only and only when
/// <c>ApexPost:DemoSeed:Enabled</c> is true. It needs a migrated, empty ApexPostDb; existing data is never modified,
/// and the schema is never created or migrated here.
/// </summary>
public sealed class DemoDataSeedHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DemoDataSeedHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("ApexPost:DemoSeed:Enabled")) return;
        var demoPassword = configuration["ApexPost:DemoSeed:Password"];
        if (string.IsNullOrWhiteSpace(demoPassword))
        {
            // The password is a user-secret, never a committed setting; scripts/setup-dev.ps1 generates one.
            logger.LogWarning("Demo seeding is enabled but ApexPost:DemoSeed:Password is not configured; no data was seeded. Run scripts/setup-dev.ps1, or: dotnet user-secrets set --project src/ApexPost.Web \"ApexPost:DemoSeed:Password\" \"<a password of at least 10 characters>\"");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(demoPassword, cancellationToken).ConfigureAwait(false);
            if (result.WasAlreadySeeded) logger.LogInformation("ApexPostDb already contains data; demo seeding was skipped.");
            else logger.LogInformation("Seeded demo data: {Branches} branches, {Staff} staff, {Products} products, {Rates} shipping rates, {Transactions} transactions.",
                result.BranchCount, result.StaffCount, result.ProductCount, result.RateCount, result.HistoricalTransactionCount);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Demo data was not seeded: {Reason}", exception.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
