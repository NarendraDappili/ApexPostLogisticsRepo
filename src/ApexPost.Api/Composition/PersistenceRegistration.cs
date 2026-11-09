using ApexPost.Application.Abstractions;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApexPost.Api.Composition;

/// <summary>
/// Registers the Week 3 data layer the way ApexPost.Web does: one scoped <see cref="ApexPostDbContext"/> per request,
/// shared by the repositories and the unit of work, with the data-change audit and audit-column interceptors. A
/// change made through the API therefore lands in the same DataChanges history as a change made at the counter.
/// </summary>
internal static class PersistenceRegistration
{
    public static IServiceCollection AddApexPostPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationContext, ApiCorrelationContext>();
        services.AddSingleton<AuditTrailLogger>();
        services.AddScoped<AuditColumnsInterceptor>();
        services.AddScoped<DataChangeAuditInterceptor>();
        // The data-change audit runs first and records what changed; the audit-column stamps that follow are not history.
        services.AddDbContext<ApexPostDbContext>((provider, options) => options
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .AddInterceptors(provider.GetRequiredService<DataChangeAuditInterceptor>(), provider.GetRequiredService<AuditColumnsInterceptor>()));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
