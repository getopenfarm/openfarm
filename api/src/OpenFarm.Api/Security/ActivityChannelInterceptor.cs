using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Api.Security;

/// <summary>
/// Stamps every operator activity row written during a token or app request with the credential it came through,
/// so each write site (post-processor or direct <c>ActivityLogEntries.Add</c>) is covered in one place.
/// Background work has no HTTP context and stays unstamped.
/// </summary>
public sealed class ActivityChannelInterceptor(IHttpContextAccessor http) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null || CredentialClaims.Channel(http.HttpContext?.User) is not { } via) return;
        foreach (var entry in context.ChangeTracker.Entries<ActivityLogEntry>())
            if (entry.State == EntityState.Added) entry.Entity.AttributeTo(via);
    }
}
