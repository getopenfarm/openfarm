using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

public sealed record SsoConfigurationValues
{
    public bool Enabled { get; init; }
    public bool Required { get; init; }
    public string DisplayName { get; init; } = "authentik";
    public string? Authority { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public int SessionHours { get; init; } = 8;
    public Guid[] RecoveryUserIds { get; init; } = [];
    public bool JitEnabled { get; init; }
    public bool RequireVerifiedEmail { get; init; } = true;
    public bool ScimEnabled { get; init; }
    public string? ScimToken { get; init; }
    public int ScimRequestsPerMinute { get; init; } = 300;
    public string DefaultGroup { get; init; } = "Viewers";
    public Dictionary<string, string> GroupMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IConfiguration Configuration()
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:Oidc:Enabled"] = Enabled.ToString(),
            ["Auth:Oidc:Required"] = Required.ToString(),
            ["Auth:Oidc:DisplayName"] = DisplayName,
            ["Auth:Oidc:Authority"] = Authority,
            ["Auth:Oidc:ClientId"] = ClientId,
            ["Auth:Oidc:ClientSecret"] = ClientSecret,
            ["Auth:Oidc:SessionHours"] = SessionHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Auth:Oidc:Jit:Enabled"] = JitEnabled.ToString(),
            ["Auth:Oidc:Jit:RequireVerifiedEmail"] = RequireVerifiedEmail.ToString(),
            ["Auth:Scim:Enabled"] = ScimEnabled.ToString(),
            ["Auth:Scim:BearerToken"] = ScimToken,
            ["Auth:Provisioning:DefaultGroup"] = DefaultGroup,
        };
        for (var i = 0; i < RecoveryUserIds.Length; i++) values[$"Auth:Oidc:RecoveryUserIds:{i}"] = RecoveryUserIds[i].ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static SsoConfigurationValues FromConfiguration(IConfiguration configuration)
    {
        var limit = configuration.GetValue("Auth:Scim:RequestsPerMinute", 300);
        if (limit is < 1 or > 10000) throw new InvalidOperationException("SCIM RequestsPerMinute must be 1–10000.");
        var oidc = new OidcSettings(configuration);
        var provisioning = new ProvisioningSettings(configuration, oidc);
        return new()
        {
            Enabled = oidc.Enabled,
            Required = oidc.Required,
            DisplayName = oidc.DisplayName,
            Authority = oidc.Authority,
            ClientId = oidc.ClientId,
            ClientSecret = oidc.ClientSecret,
            SessionHours = oidc.SessionHours,
            RecoveryUserIds = oidc.RecoveryUserIds,
            JitEnabled = provisioning.JitEnabled,
            RequireVerifiedEmail = provisioning.RequireVerifiedEmail,
            ScimEnabled = provisioning.ScimEnabled,
            ScimToken = provisioning.ScimToken,
            ScimRequestsPerMinute = limit,
            DefaultGroup = provisioning.DefaultGroup,
            GroupMappings = new(provisioning.GroupMappings, StringComparer.OrdinalIgnoreCase),
        };
    }
}

/// <summary>One database snapshot per request. The fixed-value constructor supports isolated protocol hosts.</summary>
public sealed class AuthenticationConfiguration
{
    private readonly OpenFarmDbContext? _db;
    private readonly IConfiguration? _deployment;
    private readonly IDataProtector? _protector;
    private SsoConfigurationValues? _values;
    public Guid Revision { get; private set; }
    public bool RecoveryMode => _deployment?.GetValue("Auth:Oidc:RecoveryMode", false) == true;
    public SsoConfigurationValues Values
    {
        get
        {
            if (_values is null) Load(_db!.SsoConfigurations.AsNoTracking().Single(s => s.Id == SsoConfiguration.SingletonId));
            return _values!;
        }
    }
    public OidcSettings Oidc
    {
        get
        {
            var value = RecoveryMode ? Values with { Enabled = false, Required = false, JitEnabled = false, ScimEnabled = false } : Values;
            return new(value.Configuration());
        }
    }
    public ProvisioningSettings Provisioning => new((RecoveryMode ? Values with { JitEnabled = false, ScimEnabled = false } : Values).Configuration(), Oidc, Values.GroupMappings);

    public AuthenticationConfiguration(OpenFarmDbContext db, IDataProtectionProvider protection, IConfiguration deployment)
    {
        _db = db; _deployment = deployment;
        _protector = protection.CreateProtector("OpenFarm.SsoConfiguration.v1");
    }
    public AuthenticationConfiguration(SsoConfigurationValues values) => _values = values;
    public string Protect(SsoConfigurationValues values) => _protector!.Protect(JsonSerializer.Serialize(values));
    public SsoConfigurationValues Unprotect(SsoConfiguration saved) =>
        JsonSerializer.Deserialize<SsoConfigurationValues>(_protector!.Unprotect(saved.ProtectedConfiguration))
        ?? throw new InvalidOperationException("Cannot read saved single sign-on settings.");
    private void Load(SsoConfiguration saved) { _values = Unprotect(saved); Revision = saved.Revision; }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_values is not null) return;
        Load(await _db!.SsoConfigurations.AsNoTracking().SingleAsync(s => s.Id == SsoConfiguration.SingletonId, ct));
    }

    public async Task ImportDeploymentAsync(CancellationToken ct)
    {
        if (_db is null) return;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426500)", ct);
        if (!await _db.SsoConfigurations.AnyAsync(ct))
        {
            _db.SsoConfigurations.Add(new() { ProtectedConfiguration = Protect(SsoConfigurationValues.FromConfiguration(_deployment!)) });
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
        var saved = await _db.SsoConfigurations.FromSqlInterpolated(
            $"SELECT * FROM \"SsoConfigurations\" WHERE \"Id\" = {SsoConfiguration.SingletonId} FOR SHARE").AsNoTracking().SingleAsync(ct);
        var current = new OidcSettings(Unprotect(saved).Configuration());
        return current.Enabled && current.SessionIdentity == identity;
    }

    // Sign-in and configuration updates serialize on this row, across replicas.
    public async Task<bool> LockCurrentAsync(CancellationToken ct)
    {
        if (_db is null) return true;
        var revision = (await _db.SsoConfigurations.FromSqlInterpolated(
            $"SELECT * FROM \"SsoConfigurations\" WHERE \"Id\" = {SsoConfiguration.SingletonId} FOR SHARE").AsNoTracking().SingleAsync(ct)).Revision;
        return revision == Revision;
    }
}
