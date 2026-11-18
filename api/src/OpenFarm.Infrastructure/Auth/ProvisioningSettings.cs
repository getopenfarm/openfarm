
namespace OpenFarm.Infrastructure.Auth;

public sealed class ProvisioningSettings
{
    public bool JitEnabled { get; }
    public bool RequireVerifiedEmail { get; }
    public bool ScimEnabled { get; }
    public string? ScimToken { get; }
    public string DefaultGroup { get; }
    public IReadOnlyDictionary<string, string> GroupMappings { get; }

    public ProvisioningSettings(AuthenticationSettingsValues value, OidcSettings oidc)
    {
        JitEnabled = value.JitEnabled;
        RequireVerifiedEmail = value.RequireVerifiedEmail;
        ScimEnabled = value.ScimEnabled;
        ScimToken = value.ScimToken;
        DefaultGroup = value.DefaultGroup;
        GroupMappings = new Dictionary<string, string>(value.GroupMappings, StringComparer.OrdinalIgnoreCase);
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
