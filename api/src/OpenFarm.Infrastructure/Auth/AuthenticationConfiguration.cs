using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

/// <summary>An identity-provider group name (exact, case-sensitive) and the OpenFarm group it grants.</summary>
public sealed record GroupMapping(string ExternalGroup, string Group);

public sealed record AuthenticationSettingsValues
{
    public bool OidcEnabled { get; init; }
    public bool InternalAuthenticationEnabled { get; init; } = true;
    public string DisplayName { get; init; } = "";
    public string? Authority { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public Guid[] RecoveryUserIds { get; init; } = [];
    public bool JitEnabled { get; init; }
    public bool RequireVerifiedEmail { get; init; } = true;
    public string DefaultGroup { get; init; } = "Viewers";
    public GroupMapping[] GroupMappings { get; init; } = [];
    /// <summary>The one ID-token claim that lists the user's provider groups or roles. Blobs saved before this field read as "groups".</summary>
    public string GroupsClaim { get; init; } = "groups";
}

/// <summary>One database snapshot per request. The fixed-value constructor supports isolated protocol hosts.</summary>
public sealed class AuthenticationConfiguration
{
    private readonly OpenFarmDbContext? _db;
    private readonly IConfiguration? _deployment;
    private readonly IDataProtector? _protector;
    private AuthenticationSettingsValues? _values;
    public Guid Revision { get; private set; }
    public bool RecoveryMode => _deployment?.GetValue("Auth:RecoveryMode", false) == true;
    public AuthenticationSettingsValues Values
    {
        get
        {
            if (_values is null) Load(_db!.AuthenticationSettings.AsNoTracking().Single(s => s.Id == AuthenticationSettingsRecord.SingletonId));
            return _values!;
        }
    }
    public OidcSettings Oidc
    {
        get
        {
            var value = RecoveryMode ? Values with { OidcEnabled = false, InternalAuthenticationEnabled = true, JitEnabled = false } : Values;
            return new(value);
        }
    }
    public ProvisioningSettings Provisioning => new(RecoveryMode ? Values with { JitEnabled = false } : Values, Oidc);

    public AuthenticationConfiguration(OpenFarmDbContext db, IDataProtectionProvider protection, IConfiguration deployment)
    {
        _db = db; _deployment = deployment;
        _protector = protection.CreateProtector("OpenFarm.AuthenticationSettings.v1");
    }
    public AuthenticationConfiguration(AuthenticationSettingsValues values) => _values = values;
    public string Protect(AuthenticationSettingsValues values) => _protector!.Protect(JsonSerializer.Serialize(values));
    public AuthenticationSettingsValues Unprotect(AuthenticationSettingsRecord saved) =>
        JsonSerializer.Deserialize<AuthenticationSettingsValues>(_protector!.Unprotect(saved.ProtectedConfiguration))
        ?? throw new InvalidOperationException("Cannot read saved authentication settings.");
    private void Load(AuthenticationSettingsRecord saved) { _values = Unprotect(saved); Revision = saved.Revision; }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_values is not null) return;
        Load(await _db!.AuthenticationSettings.AsNoTracking().SingleAsync(s => s.Id == AuthenticationSettingsRecord.SingletonId, ct));
    }

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        if (_db is null) return;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426500)", ct);
        if (!await _db.AuthenticationSettings.AnyAsync(ct))
        {
            _db.AuthenticationSettings.Add(new() { ProtectedConfiguration = Protect(new AuthenticationSettingsValues()) });
            await _db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        await InitializeAsync(ct);
    }

    // Logout depends on the provider identity, rather than unrelated UI or provisioning edits.
    public async Task<bool> LockProviderAsync(string identity, CancellationToken ct)
    {
        if (RecoveryMode) return false;
        if (_db is null) return Oidc.Enabled && Oidc.SessionIdentity == identity;
        var saved = await _db.AuthenticationSettings.FromSqlInterpolated(
            $"SELECT * FROM \"AuthenticationSettings\" WHERE \"Id\" = {AuthenticationSettingsRecord.SingletonId} FOR SHARE").AsNoTracking().SingleAsync(ct);
        var current = new OidcSettings(Unprotect(saved));
        return current.Enabled && current.SessionIdentity == identity;
    }

    // Sign-in and configuration updates serialize on this row, across replicas.
    public async Task<bool> LockCurrentAsync(CancellationToken ct)
    {
        if (_db is null) return true;
        var revision = (await _db.AuthenticationSettings.FromSqlInterpolated(
            $"SELECT * FROM \"AuthenticationSettings\" WHERE \"Id\" = {AuthenticationSettingsRecord.SingletonId} FOR SHARE").AsNoTracking().SingleAsync(ct)).Revision;
        return revision == Revision;
    }
}
