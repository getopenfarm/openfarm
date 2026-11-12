namespace OpenFarm.Domain.Settings;

public enum McpAccessMode { Disabled, AccessTokens, SignIn }

/// <summary>Installation security settings, separate from delegable farm defaults.</summary>
public sealed class ServerSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");
    public Guid Id { get; private set; } = SingletonId;
    public string? PublicUrl { get; private set; }
    public McpAccessMode McpMode { get; private set; }
    public string? DeploymentFingerprint { get; private set; }
    public Guid Revision { get; private set; } = Guid.NewGuid();

    public void ApplyDeploymentPolicy(string fingerprint)
    {
        if (DeploymentFingerprint == fingerprint) return;
        DeploymentFingerprint = fingerprint;
        Revision = Guid.NewGuid();
    }

    public void Update(string? publicUrl, McpAccessMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (PublicUrl == publicUrl && McpMode == mode) return;
        PublicUrl = publicUrl;
        McpMode = mode;
        // Invalidates pending OAuth requests and existing grants, including after switching back.
        Revision = Guid.NewGuid();
    }
}
