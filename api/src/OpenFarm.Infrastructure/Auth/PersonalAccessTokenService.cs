using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record AccessTokenInput(string Name, string[] Permissions, DateOnly? ExpiresOn);
public sealed record AccessTokenView(Guid Id, string Name, string LastFour, string[] Permissions,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? LastUsedAtUtc);
public sealed record AccessTokenCreated(string Credential, AccessTokenView Token);

public sealed class PersonalAccessTokenService(OpenFarmDbContext db, PermissionService permissions)
{
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create("0123456789abcdef");
    public const string Prefix = "ofp_";
    public const int MaximumTokens = 50;
    public static string Hash(string credential) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
    private static string Mint() => Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool IsWellFormed(string credential) => credential.Length == 68 && credential.StartsWith(Prefix, StringComparison.Ordinal)
        && credential.AsSpan(4).IndexOfAnyExcept(HexCharacters) < 0;

    public static PermissionDefinition[] Catalog => PermissionCatalog.All.Where(p =>
        !PermissionCatalog.Areas.Any(a => a.Key == p.Area && a.AdministratorOnly)).ToArray();

    public async Task<AccessTokenView[]> ListAsync(Guid userId, CancellationToken ct) =>
        (await db.PersonalAccessTokens.AsNoTracking().Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .OrderByDescending(t => t.CreatedAtUtc).ToArrayAsync(ct)).Select(View).ToArray();

    private static AccessTokenView View(PersonalAccessToken t) => new(t.Id, t.Name, t.LastFour, t.Permissions,
        t.CreatedAtUtc, t.ExpiresAtUtc, t.LastUsedAtUtc);

    // The owner row serializes creation, regeneration and account credential changes.
    private async Task LockOwnerAsync(Guid owner, CancellationToken ct, bool requireEnabled = true)
    {
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {owner} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is null) throw new KeyNotFoundException("User not found.");
        if (requireEnabled && !user.IsEnabled) throw new ArgumentException("This account is disabled.");
    }

    private async Task<string[]> ValidatePermissionsAsync(Guid owner, string[]? selected, CancellationToken ct)
    {
        if (selected is null || selected.Length is 0 or > 200) throw new ArgumentException("Choose at least one permission (at most 200).");
        var access = await permissions.ResolveAsync(owner, ct);
        var allowed = Catalog.Select(p => p.Key).Intersect(access.Permissions, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(p => !allowed.Contains(p))) throw new ArgumentException("A token can only have permissions its owner currently holds and may hand on.");
        return selected.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<AccessTokenCreated> CreateAsync(Guid owner, AccessTokenInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) throw new ArgumentException("Give the token a name of 1–100 characters.");
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiry = input.ExpiresOn is { } day ? new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Utc)) : null;
        if (expiry <= now) throw new ArgumentException("Choose an expiry date in the future.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(owner, ct);
        var live = db.PersonalAccessTokens.Where(t => t.UserId == owner && t.RevokedAtUtc == null);
        if (await live.CountAsync(ct) >= MaximumTokens) throw new ArgumentException("A user can hold at most 50 tokens. Revoke one first.");
        if (await live.AnyAsync(t => t.Name == name, ct)) throw new ArgumentException("There is already a token with that name.");
        var selected = await ValidatePermissionsAsync(owner, input.Permissions, ct);
        var credential = Mint();
        var row = new PersonalAccessToken { Id = Guid.NewGuid(), UserId = owner, Name = name,
            CredentialHash = Hash(credential), LastFour = credential[^4..], Permissions = selected, CreatedAtUtc = now, ExpiresAtUtc = expiry };
        db.PersonalAccessTokens.Add(row);
        Audit(owner, row, "AccessTokenCreated");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(credential, View(row));
    }

    public async Task<AccessTokenCreated> RegenerateAsync(Guid owner, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(owner, ct);
        var row = await db.PersonalAccessTokens.SingleOrDefaultAsync(t => t.Id == id && t.UserId == owner && t.RevokedAtUtc == null, ct)
            ?? throw new KeyNotFoundException("That token no longer exists.");
        if (row.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new ArgumentException("This token has expired. Create a new one.");
        await ValidatePermissionsAsync(owner, row.Permissions, ct);
        var credential = Mint();
        row.CredentialHash = Hash(credential);
        row.LastFour = credential[^4..];
        row.LastUsedAtUtc = null;
        Audit(owner, row, "AccessTokenRegenerated");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(credential, View(row));
    }

    // Also called inside the account service's transaction after locking/updating the user.
    public async Task RevokeAsync(Guid owner, Guid? id, Guid actor, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockOwnerAsync(owner, ct, requireEnabled: false);
            await RevokeAsync(owner, id, actor, ct);
            await tx.CommitAsync(ct);
            return;
        }
        var query = db.PersonalAccessTokens.Where(t => t.UserId == owner && t.RevokedAtUtc == null);
        if (id is not null) query = query.Where(t => t.Id == id);
        var rows = await query.AsNoTracking().ToArrayAsync(ct);
        if (id is not null && rows.Length == 0) throw new KeyNotFoundException("That token no longer exists.");
        await query.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTimeOffset.UtcNow), ct);
        foreach (var row in rows) Audit(actor, row, "AccessTokenRevoked");
        await db.SaveChangesAsync(ct);
    }

    public async Task<PersonalAccessToken?> AuthenticateAsync(string credential, CancellationToken ct)
    {
        if (!IsWellFormed(credential)) return null;
        var hash = Hash(credential);
        var now = DateTimeOffset.UtcNow;
        var row = await db.PersonalAccessTokens.AsNoTracking().SingleOrDefaultAsync(t => t.CredentialHash == hash
            && t.RevokedAtUtc == null && (t.ExpiresAtUtc == null || t.ExpiresAtUtc > now), ct);
        if (row is null || !await db.Users.AnyAsync(u => u.Id == row.UserId && u.IsEnabled, ct)) return null;
        // Conditional update cannot restore a revoked or regenerated credential. At most one write/minute.
        await db.PersonalAccessTokens.Where(t => t.Id == row.Id && t.CredentialHash == hash && t.RevokedAtUtc == null
            && (t.LastUsedAtUtc == null || t.LastUsedAtUtc < now.AddMinutes(-1)))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAtUtc, now), ct);
        return row;
    }

    private void Audit(Guid actor, PersonalAccessToken row, string action) => db.ActivityLogEntries.Add(
        ActivityLogEntry.Create("AccessToken", row.Id.ToString(), action, actor, ActorType.Operator,
            new ActivityLogDetails(ReportedState: System.Text.Json.JsonSerializer.Serialize(new { row.Name, row.UserId, row.Permissions, row.ExpiresAtUtc, row.LastFour }))));
}
