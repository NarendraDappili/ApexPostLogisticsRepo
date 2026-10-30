using ApexPost.Application.Abstractions;
using ApexPost.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.Persistence;

/// <summary>
/// Fills the CreatedAtUtc, CreatedBy and ModifiedAtUtc shadow columns on every save, so neither the domain
/// nor the repositories handle audit columns and a posted form can never set them.
/// </summary>
public sealed class AuditColumnsInterceptor(TimeProvider timeProvider, ICurrentStaffContext? currentStaff = null) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var actor = currentStaff?.StaffId?.Value ?? ShadowColumns.SystemActor;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty(ShadowColumns.CreatedAtUtc) is null) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(ShadowColumns.CreatedAtUtc).CurrentValue = now;
                    entry.Property(ShadowColumns.CreatedBy).CurrentValue = actor;
                    break;
                case EntityState.Modified:
                    entry.Property(ShadowColumns.ModifiedAtUtc).CurrentValue = now;
                    // Creation stamps are immutable even if a caller tried to change them.
                    Preserve(entry, ShadowColumns.CreatedAtUtc);
                    Preserve(entry, ShadowColumns.CreatedBy);
                    break;
            }
        }
    }

    private static void Preserve(EntityEntry entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        property.CurrentValue = property.OriginalValue;
        property.IsModified = false;
    }
}
