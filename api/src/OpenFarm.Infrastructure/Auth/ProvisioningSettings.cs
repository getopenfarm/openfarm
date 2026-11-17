using Microsoft.Extensions.Configuration;

namespace OpenFarm.Infrastructure.Auth;

public sealed class ProvisioningSettings
{
    public bool JitEnabled { get; }
    public bool RequireVerifiedEmail { get; }
    public bool ScimEnabled { get; }
    public string? ScimToken { get; }
    public string DefaultGroup { get; }
    public IReadOnlyDictionary<string, string> GroupMappings { get; }

    public ProvisioningSettings(IConfiguration configuration, OidcSettings oidc)
    {
        JitEnabled = configuration.GetValue("Auth:Oidc:Jit:Enabled", false);
        RequireVerifiedEmail = configuration.GetValue("Auth:Oidc:Jit:RequireVerifiedEmail", true);
        ScimEnabled = configuration.GetValue("Auth:Scim:Enabled", false);
        ScimToken = configuration["Auth:Scim:BearerToken"];
        DefaultGroup = configuration["Auth:Provisioning:DefaultGroup"] ?? "Viewers";
        GroupMappings = new Dictionary<string, string>(configuration.GetSection("Auth:Scim:GroupMappings")
            .Get<Dictionary<string, string>>() ?? [], StringComparer.OrdinalIgnoreCase);
        if ((JitEnabled || ScimEnabled) && !oidc.Enabled)
            throw new InvalidOperationException("JIT and SCIM provisioning require an enabled OIDC provider.");
        if (ScimEnabled && (string.IsNullOrWhiteSpace(ScimToken) || ScimToken.Length is < 32 or > 512
            || ScimToken.Any(char.IsWhiteSpace) || ScimToken.Contains('.')
            || ScimToken.StartsWith(PersonalAccessTokenService.Prefix, StringComparison.OrdinalIgnoreCase)
            || ScimToken.StartsWith(BrowserSessionService.AppCredentialPrefix, StringComparison.OrdinalIgnoreCase)
            || ScimToken == oidc.ClientSecret))
            throw new InvalidOperationException("SCIM needs a separate random BearerToken of 32–512 characters, without whitespace or dots. Do not reuse application tokens or the OIDC credential.");
        if ((JitEnabled || ScimEnabled) && (string.IsNullOrWhiteSpace(DefaultGroup) || DefaultGroup.Length > 256
            || GroupMappings.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 256)))
            throw new InvalidOperationException("Provisioning group names must contain 1–256 characters.");
    }
}
