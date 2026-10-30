using ApexPost.Application.Abstractions;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Seed;

namespace ApexPost.Web.Services;

/// <summary>
/// Seeds the synthetic demo data set before the server accepts requests, in Development only and only when
/// <c>ApexPost:DemoSeed:Enabled</c> is true. Existing non-demo data is never modified.
/// </summary>
public sealed class DemoDataSeedHostedService(
    JsonRepositoryOptions options,
    IPasswordHasher passwordHasher,
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
            logger.LogWarning("Demo seeding is enabled but ApexPost:DemoSeed:Password is not configured; no data was seeded.");
            return;
        }

        try
        {
            var result = await new DemoDataSeeder(options, demoPassword, passwordHasher).SeedAsync(cancellationToken).ConfigureAwait(false);
            if (result.WasAlreadySeeded) logger.LogInformation("Demo data is already seeded in {DataDirectory}.", options.DataDirectory);
            else logger.LogInformation("Seeded demo data: {Branches} branches, {Staff} staff, {Products} products, {Rates} shipping rates, {Transactions} transactions.",
                result.BranchCount, result.StaffCount, result.ProductCount, result.RateCount, result.HistoricalTransactionCount);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Demo data was not seeded: {Reason} Point ApexPost:DataDirectory at an empty folder to get the demo data set.", exception.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
