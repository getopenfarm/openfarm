using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed class ScimService(OpenFarmDbContext db, UserManager<ApplicationUser> users, OidcSettings oidc,
    ExternalProvisioningService provisioning, TimeProvider time, AuthenticationConfiguration configuration)
{
    private IQueryable<ApplicationUser> UserQuery => db.Users.Where(u => u.ProvisioningProvider == oidc.ProviderKey && !u.IsScimDeleted);
    private IQueryable<ProvisioningGroup> GroupQuery => db.ProvisioningGroups.Where(g => g.Provider == oidc.ProviderKey);

    public async Task<JsonObject> ListAsync(bool group, string? filter, int start, int count, string origin, CancellationToken ct)
    {
        var parsed = ScimProtocol.Filter(filter, group);
        var normalizedFilter = parsed is { } term ? term.Value.ToUpperInvariant() : null;
        if (group)
        {
            var query = GroupQuery;
            if (parsed is { } f) query = f.Attribute switch
            {
                "ID" => Guid.TryParse(f.Value, out var id) ? query.Where(g => g.Id == id) : query.Where(g => false),
                "EXTERNALID" => query.Where(g => g.ExternalId == f.Value),
                _ => query.Where(g => g.NormalizedDisplayName == normalizedFilter),
            };
            var total = await query.CountAsync(ct);
            var page = await query.AsNoTracking().OrderBy(g => g.Id).Skip(start - 1).Take(count).ToListAsync(ct);
            var resources = new List<JsonObject>();
            foreach (var item in page) resources.Add(await GroupResourceAsync(item, origin, ct));
            return ScimProtocol.List(resources, total, start);
        }
        else
        {
            var query = UserQuery;
            if (parsed is { } f) query = f.Attribute switch
            {
                "ID" => Guid.TryParse(f.Value, out var id) ? query.Where(u => u.Id == id) : query.Where(u => false),
                "EXTERNALID" => query.Where(u => u.ProvisioningExternalId == f.Value),
                _ => query.Where(u => u.NormalizedUserName == normalizedFilter),
            };
            var total = await query.CountAsync(ct);
            var page = await query.AsNoTracking().OrderBy(u => u.Id).Skip(start - 1).Take(count).ToListAsync(ct);
            var resources = new List<JsonObject>();
            foreach (var item in page) resources.Add(await UserResourceAsync(item, origin, ct));
            return ScimProtocol.List(resources, total, start);
        }
    }

    public async Task<JsonObject> ReadAsync(bool group, Guid id, string origin, CancellationToken ct) => group
        ? await GroupResourceAsync(await FindGroupAsync(id, ct), origin, ct)
        : await UserResourceAsync(await FindUserAsync(id, ct), origin, ct);

    public async Task<JsonObject?> WriteAsync(bool group, string method, Guid? id, JsonObject? body,
        string? ifMatch, string origin, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await provisioning.LockAsync(ct);
        if (!await configuration.LockCurrentAsync(ct)) throw new ScimException(409, "Provisioning settings changed. Retry the request.");
        JsonObject? result;
        if (group) result = await WriteGroupAsync(method, id, body, ifMatch, origin, ct);
        else result = await WriteUserAsync(method, id, body, ifMatch, origin, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<JsonObject?> WriteUserAsync(string method, Guid? id, JsonObject? body, string? ifMatch, string origin, CancellationToken ct)
    {
        ApplicationUser? user = id is null ? null : await FindUserAsync(id.Value, ct);
        if (user is not null)
        {
            if (oidc.RecoveryUserIds.Contains(user.Id)) throw new ScimException(403, "Recovery accounts cannot be provisioned.");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE", ct);
            await db.Entry(user).ReloadAsync(ct);
            ScimProtocol.CheckVersion(ifMatch, user.ProvisioningRevision);
        }
        if (method == "DELETE")
        {
            user!.IsEnabled = false;
            user.IsScimDeleted = true;
            await RevokeAsync(user, ct);
            await TouchRelatedGroupsAsync(user.Id, ct);
            await db.ProvisioningGroupMembers.Where(m => m.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.UserRoles.Where(m => m.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.UserClaims.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
            Touch(user);
            Audit("User", user.Id, "ScimUserDeleted");
            await EnsureAdministratorAsync(ct);
            return null;
        }
        var document = body ?? throw new ScimException(400, "A resource body is required.", "invalidSyntax");
        if (method == "PATCH") document = ScimPatch.Apply(await UserResourceAsync(user!, origin, ct), document, false);
        else ScimProtocol.RequireSchema(document, ScimProtocol.UserSchema);
        if (ScimProtocol.Has(document, "password") || ScimProtocol.Has(document, "roles") || ScimProtocol.Has(document, "entitlements"))
            throw new ScimException(400, "Credentials and application permissions cannot be set through SCIM.", "mutability");
        var username = ScimProtocol.Required(document, "userName").Trim();
        var externalId = ScimProtocol.Required(document, "externalId", 255);
        if (user is not null && externalId != user.ProvisioningExternalId) throw new ScimException(400, "externalId is the immutable OIDC subject.", "mutability");
        var emails = ScimProtocol.Get(document, "emails") as JsonArray;
        if (emails is null || emails.Count != 1 || emails.Any(e => e is not JsonObject))
            throw new ScimException(400, "Exactly one account email is required.", "invalidValue");
        var primary = emails.OfType<JsonObject>().Where(e => ScimProtocol.Boolean(e, "primary") == true).ToArray();
        if (primary.Length > 1) throw new ScimException(400, "Only one primary email is allowed.", "invalidValue");
        var email = ScimProtocol.Required(primary.FirstOrDefault() ?? (JsonObject)emails[0]!, "value").Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new ScimException(400, "Invalid email address.", "invalidValue");
        var name = ScimProtocol.Get(document, "name") as JsonObject;
        if (ScimProtocol.Get(document, "name") is not null && name is null) throw new ScimException(400, "name must be an object.", "invalidValue");
        var given = name is null ? null : ScimProtocol.Text(name, "givenName");
        var family = name is null ? null : ScimProtocol.Text(name, "familyName");
        var display = ScimProtocol.Text(document, "displayName") ?? (name is null ? null : ScimProtocol.Text(name, "formatted"));
        if (display is null && (given is not null || family is not null)) display = $"{given} {family}".Trim();
        var active = ScimProtocol.Boolean(document, "active") ?? user?.IsEnabled ?? true;
        var created = user is null;
        if (created) user = await provisioning.RestoreScimAsync(externalId, username, email, display, active, ct)
            ?? await provisioning.CreateAsync(externalId, username, email, display, active, "ScimUserCreated", ct);
        else
        {
            user!.UserName = username; user.Email = email; user.DisplayName = display; user.IsEnabled = active;
            Check(await users.UpdateAsync(user));
        }
        user!.ProvisioningGivenName = given; user.ProvisioningFamilyName = family;
        Touch(user);
        if (!active) await RevokeAsync(user, ct);
        if (!created) Audit("User", user.Id, "ScimUserUpdated");
        if (!created) await TouchRelatedGroupsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        await EnsureAdministratorAsync(ct);
        return await UserResourceAsync(user, origin, ct);
    }

    private async Task<JsonObject?> WriteGroupAsync(string method, Guid? id, JsonObject? body, string? ifMatch, string origin, CancellationToken ct)
    {
        var group = id is null ? null : await FindGroupAsync(id.Value, ct);
        if (group is not null) ScimProtocol.CheckVersion(ifMatch, group.Revision);
        if (method == "DELETE")
        {
            await TouchMembersAsync(group!.Id, [], ct);
            db.ProvisioningGroups.Remove(group!);
            Audit("ProvisioningGroup", group!.Id, "ScimGroupDeleted");
            return null;
        }
        var document = body ?? throw new ScimException(400, "A resource body is required.", "invalidSyntax");
        if (method == "PATCH") document = ScimPatch.Apply(await GroupResourceAsync(group!, origin, ct), document, true);
        else ScimProtocol.RequireSchema(document, ScimProtocol.GroupSchema);
        var displayName = ScimProtocol.Required(document, "displayName").Trim();
        var externalId = ScimProtocol.Text(document, "externalId", 255);
        var normalized = displayName.ToUpperInvariant();
        if (group is not null && group.ExternalId != externalId) throw new ScimException(400, "externalId cannot be changed.", "mutability");
        var existingId = group?.Id ?? Guid.Empty;
        if (await GroupQuery.AnyAsync(g => g.Id != existingId
            && (g.NormalizedDisplayName == normalized || (externalId != null && g.ExternalId == externalId)), ct))
            throw new ScimException(409, "The group name or external ID is already in use.", "uniqueness");
        var members = ScimProtocol.Get(document, "members") as JsonArray;
        if (ScimProtocol.Get(document, "members") is not null && members is null) throw new ScimException(400, "members must be an array.", "invalidValue");
        if (members?.Count > 1000) throw new ScimException(413, "A group can contain at most 1000 members.", "tooMany");
        var ids = new HashSet<Guid>();
        foreach (var member in members ?? [])
        {
            if (member is not JsonObject m || !Guid.TryParse(ScimProtocol.Text(m, "value"), out var memberId))
                throw new ScimException(400, "Each member must reference an OpenFarm SCIM user ID.", "invalidValue");
            ids.Add(memberId);
        }
        if (await UserQuery.CountAsync(u => ids.Contains(u.Id) && !oidc.RecoveryUserIds.Contains(u.Id), ct) != ids.Count)
            throw new ScimException(400, "A member does not reference a provisioned user.", "invalidValue");
        var created = group is null;
        group ??= new ProvisioningGroup { Provider = oidc.ProviderKey, CreatedAtUtc = time.GetUtcNow() };
        group.ExternalId = externalId; group.DisplayName = displayName; group.NormalizedDisplayName = normalized;
        group.ModifiedAtUtc = time.GetUtcNow(); group.Revision = Guid.NewGuid();
        await TouchMembersAsync(group.Id, ids, ct);
        if (created) db.ProvisioningGroups.Add(group);
        else await db.ProvisioningGroupMembers.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync(ct);
        db.ProvisioningGroupMembers.AddRange(ids.Select(userId => new ProvisioningGroupMember { GroupId = group.Id, UserId = userId }));
        Audit("ProvisioningGroup", group.Id, created ? "ScimGroupCreated" : "ScimGroupUpdated");
        await db.SaveChangesAsync(ct);
        return await GroupResourceAsync(group, origin, ct);
    }

    private Task<ApplicationUser> FindUserAsync(Guid id, CancellationToken ct) => FindAsync(UserQuery, id, ct);
    private async Task<ProvisioningGroup> FindGroupAsync(Guid id, CancellationToken ct) =>
        await GroupQuery.SingleOrDefaultAsync(g => g.Id == id, ct) ?? throw new ScimException(404, "Group not found.");
    private static async Task<ApplicationUser> FindAsync(IQueryable<ApplicationUser> query, Guid id, CancellationToken ct) =>
        await query.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new ScimException(404, "User not found.");

    private async Task<JsonObject> UserResourceAsync(ApplicationUser user, string origin, CancellationToken ct)
    {
        var memberships = await (from member in db.ProvisioningGroupMembers
                                 join externalGroup in GroupQuery on member.GroupId equals externalGroup.Id
                                 where member.UserId == user.Id
                                 orderby externalGroup.Id
                                 select externalGroup).AsNoTracking().ToListAsync(ct);
        return new()
        {
            ["schemas"] = new JsonArray(ScimProtocol.UserSchema),
            ["id"] = user.Id.ToString(),
            ["externalId"] = user.ProvisioningExternalId,
            ["userName"] = user.UserName,
            ["displayName"] = user.DisplayName,
            ["active"] = user.IsEnabled,
            ["name"] = new JsonObject { ["givenName"] = user.ProvisioningGivenName, ["familyName"] = user.ProvisioningFamilyName, ["formatted"] = user.DisplayName },
            ["emails"] = new JsonArray(new JsonObject { ["value"] = user.Email, ["type"] = "work", ["primary"] = true }),
            ["groups"] = new JsonArray(memberships.Select(g => (JsonNode)new JsonObject
            {
                ["value"] = g.Id.ToString(),
                ["display"] = g.DisplayName,
                ["type"] = "direct",
                ["$ref"] = Location(origin, true, g.Id)
            }).ToArray()),
            ["meta"] = Meta("User", user.CreatedAtUtc, user.ProvisioningModifiedAtUtc ?? user.CreatedAtUtc, user.ProvisioningRevision, Location(origin, false, user.Id)),
        };
    }

    private async Task<JsonObject> GroupResourceAsync(ProvisioningGroup group, string origin, CancellationToken ct)
    {
        var groupId = group.Id;
        var members = await (from member in db.ProvisioningGroupMembers
                             join user in UserQuery on member.UserId equals user.Id
                             where member.GroupId == groupId
                             orderby user.Id
                             select user).AsNoTracking().ToListAsync(ct);
        return new()
        {
            ["schemas"] = new JsonArray(ScimProtocol.GroupSchema),
            ["id"] = group.Id.ToString(),
            ["externalId"] = group.ExternalId,
            ["displayName"] = group.DisplayName,
            ["members"] = new JsonArray(members.Select(u => (JsonNode)new JsonObject
            {
                ["value"] = u.Id.ToString(),
                ["display"] = u.UserName,
                ["$ref"] = Location(origin, false, u.Id)
            }).ToArray()),
            ["meta"] = Meta("Group", group.CreatedAtUtc, group.ModifiedAtUtc, group.Revision, Location(origin, true, group.Id)),
        };
    }

    private async Task RevokeAsync(ApplicationUser user, CancellationToken ct)
    {
        // All credential types are revoked in the same transaction as deactivation. Re-enabling never restores them.
        var now = time.GetUtcNow();
        await db.BrowserSessions.Where(s => s.UserId == user.Id && s.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.PersonalAccessTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.UserTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync(ct);
        user.SecurityStamp = Guid.NewGuid().ToString();
    }

    private async Task TouchMembersAsync(Guid groupId, IEnumerable<Guid> nextMembers, CancellationToken ct)
    {
        var previous = await db.ProvisioningGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToArrayAsync(ct);
        var affected = previous.Concat(nextMembers).Distinct().ToArray();
        var revision = Guid.NewGuid();
        var now = time.GetUtcNow();
        await db.Users.Where(u => affected.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProvisioningRevision, revision)
            .SetProperty(u => u.ProvisioningModifiedAtUtc, now), ct);
    }

    private Task<int> TouchRelatedGroupsAsync(Guid userId, CancellationToken ct)
    {
        var groups = db.ProvisioningGroupMembers.Where(m => m.UserId == userId).Select(m => m.GroupId);
        var revision = Guid.NewGuid();
        var now = time.GetUtcNow();
        return db.ProvisioningGroups.Where(g => groups.Contains(g.Id)).ExecuteUpdateAsync(s => s.SetProperty(g => g.Revision, revision).SetProperty(g => g.ModifiedAtUtc, now), ct);
    }

    private async Task EnsureAdministratorAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (!await (from user in db.Users
                    join member in db.UserRoles on user.Id equals member.UserId
                    join role in db.Roles on member.RoleId equals role.Id
                    where user.IsEnabled && role.IsProtected && role.Name == PermissionCatalog.Administrators
                    select user.Id).AnyAsync(ct))
            throw new ScimException(409, "At least one enabled administrator must remain.");
    }

    private void Touch(ApplicationUser user)
    { user.ProvisioningModifiedAtUtc = time.GetUtcNow(); user.ProvisioningRevision = Guid.NewGuid(); }
    private void Audit(string resource, Guid id, string action) => db.ActivityLogEntries.Add(ActivityLogEntry.Create(resource, id.ToString(), action, null, ActorType.System));
    private static void Check(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")) throw new ScimException(409, "Username or email is already in use.", "uniqueness");
        throw new ScimException(400, "Invalid account attributes.", "invalidValue");
    }
    private static JsonObject Meta(string type, DateTimeOffset created, DateTimeOffset modified, Guid revision, string location) => new()
    { ["resourceType"] = type, ["created"] = created, ["lastModified"] = modified, ["version"] = ScimProtocol.Version(revision), ["location"] = location };
    private static string Location(string origin, bool group, Guid id) => $"{origin}/scim/v2/{(group ? "Groups" : "Users")}/{id}";
}
