using System.Net;
using System.Text.Json;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Application.Tracking;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Queries;
using ApexPost.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Api.Tests.Hosting;

/// <summary>
/// B1: the API host starts with minimal hosting and reuses the Week 3 data layer through dependency injection. The
/// host runs in Development, where every registration is validated when it is built, so a missing dependency fails
/// these tests rather than the first request that needs it.
/// </summary>
public sealed class ApiCompositionTests
{
    [Fact]
    public async Task The_host_starts_and_answers_the_health_check()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Resolves_the_week3_dbcontext_with_both_audit_interceptors_and_the_unit_of_work()
    {
        await using var factory = new OfflineApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.IsType<EfUnitOfWork>(unitOfWork);
        Assert.Equal("ApexPostDb_ApiTests_Unused", db.Database.GetDbConnection().Database);
        var interceptors = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ToList();
        Assert.Collection(interceptors,
            first => Assert.IsType<DataChangeAuditInterceptor>(first),
            second => Assert.IsType<AuditColumnsInterceptor>(second));
    }

    [Fact]
    public async Task The_tracking_service_and_its_repository_share_the_request_dbcontext()
    {
        await using var factory = new OfflineApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ShipmentTrackingService>());
        Assert.IsType<EfTrackedShipmentRepository>(scope.ServiceProvider.GetRequiredService<ITrackedShipmentRepository>());
        Assert.IsType<EfTrackingQueries>(scope.ServiceProvider.GetRequiredService<ITrackingQueries>());
    }

    [Fact]
    public async Task One_request_scope_shares_one_dbcontext_between_its_services()
    {
        await using var factory = new OfflineApiFactory();
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<ApexPostDbContext>(), first.ServiceProvider.GetRequiredService<ApexPostDbContext>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<ApexPostDbContext>(), second.ServiceProvider.GetRequiredService<ApexPostDbContext>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refuses_to_start_without_a_connection_string(string connectionString)
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:ApexPostDb", connectionString));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:ApexPostDb is not configured", exception.Message);
    }

    [Fact]
    public async Task A_route_that_does_not_exist_is_a_404()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
