using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Settings;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record ServerSettingsResponse(string? PublicUrl, string McpMode, Guid Revision,
    bool PublicUrlManaged, bool McpModeManaged, bool? McpEnabledOverride);

/// <summary>Browser administrator settings. Passwords never enter audit metadata.</summary>
public sealed class ServerSettingsService(OpenFarmDbContext db, ServerConfigurationOverrides policy,
    UserManager<ApplicationUser> users)
{
    public async Task<ServerSettingsResponse> ReadAsync(CancellationToken ct) =>
        Response(await db.ServerSettings.AsNoTracking().SingleAsync(s => s.Id == ServerSettings.SingletonId, ct));

    public async Task<ServerSettingsResponse?> UpdateAsync(Guid actor, Guid revision, string password,
        bool changeUrl, string? publicUrl, string? mode, CancellationToken ct)
    {
        if (password is null || password.Length is 0 or > 1024) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Use the same user lock as login and password reset; failed checks use Identity lockout.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {actor} FOR UPDATE").SingleAsync(ct);
        if (!user.IsEnabled || await users.IsLockedOutAsync(user)) return null;
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            await transaction.CommitAsync(ct);
            return null;
        }
        await users.ResetAccessFailedCountAsync(user);
        var saved = await db.ServerSettings.FromSqlInterpolated($"SELECT * FROM \"ServerSettings\" WHERE \"Id\" = {ServerSettings.SingletonId} FOR UPDATE").SingleAsync(ct);
        if (saved.Revision != revision) throw new ServerSettingsConflictException();
        var url = saved.PublicUrl;
        var nextMode = saved.McpMode;
        if (changeUrl)
        {
            if (policy.PublicUrl is not null) throw new ArgumentException("Public URL is managed by ORIGIN in the deployment.");
            try { url = ServerConfigurationOverrides.NormalizeOrigin(publicUrl); }
            catch (InvalidOperationException) { throw new ArgumentException("Enter an HTTP or HTTPS public URL without a path, query, credentials, or fragment."); }
        }
        else
        {
            if (policy.Enabled is not null || policy.Mode is not null) throw new ArgumentException("MCP mode is managed by the deployment.");
            if (!Enum.TryParse<McpAccessMode>(mode, false, out nextMode) || !Enum.IsDefined(nextMode) || nextMode.ToString() != mode)
                throw new ArgumentException("Select a supported MCP mode.");
        }
        // Validate the entire effective configuration before saving anything.
        try { _ = new OAuthSettings((policy.Mode ?? nextMode) == McpAccessMode.SignIn && policy.Enabled != false ? "true" : "false", policy.PublicUrl ?? url); }
        catch (InvalidOperationException) { throw new ArgumentException("Sign-in needs an HTTPS public URL. HTTP is allowed only for localhost."); }
        var previous = Response(saved);
        saved.Update(url, nextMode);
        if (saved.Revision != revision)
        {
            // A URL or mode change invalidates every app grant and pending request, across all replicas.
            var now = DateTimeOffset.UtcNow;
            await db.BrowserSessions.Where(s => s.ClientId != null && s.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("ServerSettings", saved.Id.ToString(),
                changeUrl ? "PublicUrlChanged" : "McpModeChanged", actor, ActorType.Operator,
                new ActivityLogDetails(PreviousState: JsonSerializer.Serialize(previous), ReportedState: JsonSerializer.Serialize(Response(saved)))));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Response(saved);
    }

    private ServerSettingsResponse Response(ServerSettings saved)
    {
        var effective = policy.Resolve(saved);
        return new(effective.PublicUrl, effective.McpMode.ToString(), saved.Revision, policy.PublicUrl is not null,
            policy.Enabled is not null || policy.Mode is not null, policy.Enabled);
    }
}

public sealed class ServerSettingsConflictException : Exception
{
    public ServerSettingsConflictException() : base("Server settings changed. Reload the settings and try again.") { }
}
