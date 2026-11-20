namespace OpenFarm.Infrastructure.Auth;

public sealed class ProvisioningSettings
{
    public bool JitEnabled { get; }
    public bool RequireVerifiedEmail { get; }
    public string DefaultGroup { get; }
    public GroupMapping[] GroupMappings { get; }
    public string GroupsClaim { get; }

    // Claims that carry identity or token metadata. A user can edit some of them at the provider (name, preferred_username),
    // so none may ever decide group membership.
    private static readonly HashSet<string> IdentityClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        "sub", "iss", "aud", "exp", "iat", "nbf", "nonce", "sid", "auth_time", "email", "email_verified",
        "preferred_username", "name", "azp", "acr", "amr", "at_hash", "c_hash",
    };

    public ProvisioningSettings(AuthenticationSettingsValues value, OidcSettings oidc)
    {
        JitEnabled = value.JitEnabled;
        RequireVerifiedEmail = value.RequireVerifiedEmail;
        DefaultGroup = value.DefaultGroup;
        GroupMappings = value.GroupMappings ?? [];
        GroupsClaim = value.GroupsClaim;
        if (JitEnabled && !oidc.Enabled)
            throw new InvalidOperationException("JIT provisioning requires an enabled OIDC provider.");
        if (JitEnabled && (string.IsNullOrWhiteSpace(DefaultGroup) || DefaultGroup.Length > 256))
            throw new InvalidOperationException("The provisioning group name must contain 1–256 characters.");
        if (JitEnabled && (GroupMappings.Length > 50
            || GroupMappings.Any(m => m is null || string.IsNullOrWhiteSpace(m.ExternalGroup) || m.ExternalGroup.Length > 256
                || string.IsNullOrWhiteSpace(m.Group) || m.Group.Length > 256)
            || GroupMappings.GroupBy(m => (m.ExternalGroup, m.Group.ToUpperInvariant())).Any(g => g.Count() > 1)))
            throw new InvalidOperationException("Group mappings need 1–256 characters on both sides, no duplicates, and at most 50 rows.");
        if (JitEnabled && (string.IsNullOrEmpty(GroupsClaim) || GroupsClaim.Length > 256
            || GroupsClaim.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || IdentityClaims.Contains(GroupsClaim)))
            throw new InvalidOperationException("The groups claim needs 1–256 characters without spaces, and cannot be an identity claim such as sub, email or name.");
    }
}
