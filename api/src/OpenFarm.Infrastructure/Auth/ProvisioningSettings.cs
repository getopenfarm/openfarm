namespace OpenFarm.Infrastructure.Auth;

public sealed class ProvisioningSettings
{
    public bool JitEnabled { get; }
    public bool RequireVerifiedEmail { get; }
    public string DefaultGroup { get; }

    public ProvisioningSettings(AuthenticationSettingsValues value, OidcSettings oidc)
    {
        JitEnabled = value.JitEnabled;
        RequireVerifiedEmail = value.RequireVerifiedEmail;
        DefaultGroup = value.DefaultGroup;
        if (JitEnabled && !oidc.Enabled)
            throw new InvalidOperationException("JIT provisioning requires an enabled OIDC provider.");
        if (JitEnabled && (string.IsNullOrWhiteSpace(DefaultGroup) || DefaultGroup.Length > 256))
            throw new InvalidOperationException("The provisioning group name must contain 1–256 characters.");
    }
}
