using System.Security.Cryptography;
using System.Text;

namespace OpenFarm.Infrastructure.Auth;

/// <summary>Saved login policy, independent of OpenFarm's MCP OAuth server.</summary>
public sealed class OidcSettings
{
    public bool Enabled { get; }
    public bool InternalAuthenticationEnabled { get; }
    public string DisplayName { get; }
    public string? Authority { get; }
    public string? ClientId { get; }
    public string? ClientSecret { get; }
    public int SessionHours { get; }
    public Guid[] RecoveryUserIds { get; }
    // Bind external identities to the issuer, not to an email or a mutable display name.
    public string ProviderKey => "oidc:" + Hash(Authority!);
    public string TestIdentity(string? publicUrl) => Hash(System.Text.Json.JsonSerializer.Serialize(new[] { Authority, ClientId, ClientSecret, publicUrl }));
    public string SessionIdentity => Hash($"{Authority}|{ClientId}");

    public OidcSettings(AuthenticationSettingsValues value)
    {
        Enabled = value.OidcEnabled;
        InternalAuthenticationEnabled = value.InternalAuthenticationEnabled;
        DisplayName = value.DisplayName.Trim() is { Length: > 0 } name ? name : "authentik";
        Authority = value.Authority;
        ClientId = value.ClientId;
        ClientSecret = value.ClientSecret;
        SessionHours = value.SessionHours;
        RecoveryUserIds = value.RecoveryUserIds;
        if (!InternalAuthenticationEnabled && (!Enabled || RecoveryUserIds.Length == 0))
            throw new InvalidOperationException("Disabling internal authentication requires OIDC and a recovery administrator.");
        if (DisplayName.Length > 80 || SessionHours is < 1 or > 24)
            throw new InvalidOperationException("OIDC display name must be at most 80 characters; SessionHours must be 1–24.");
        if (!Enabled) return;
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("OIDC needs an HTTPS authority, client ID, and client secret (HTTP is allowed only on loopback).");
    }

    public bool AllowsPassword(Guid userId) => InternalAuthenticationEnabled || RecoveryUserIds.Contains(userId);

    public void ValidatePublicUrl(string? value)
    {
        if (Enabled && (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))))
            throw new InvalidOperationException("OIDC requires an HTTPS public URL (HTTP is allowed only on loopback).");
    }

    public bool AllowsBrowserSession(Guid userId, string? identity) => identity is null
        ? AllowsPassword(userId)
        : Enabled && identity == SessionIdentity;

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
