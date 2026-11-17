using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace OpenFarm.Infrastructure.Auth;

/// <summary>Deployment-controlled login policy, independent of OpenFarm's MCP OAuth server.</summary>
public sealed class OidcSettings
{
    public bool Enabled { get; }
    public bool Required { get; }
    public string DisplayName { get; }
    public string? Authority { get; }
    public string? ClientId { get; }
    public string? ClientSecret { get; }
    public int SessionHours { get; }
    public Guid[] RecoveryUserIds { get; }
    // Bind external identities to the issuer, not to an email or a mutable display name.
    public string ProviderKey => "oidc:" + Hash(Authority!);
    public string SessionIdentity => Hash($"{Authority}|{ClientId}");

    public OidcSettings(IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth:Oidc");
        Enabled = section.GetValue("Enabled", false);
        Required = section.GetValue("Required", false);
        DisplayName = section["DisplayName"]?.Trim() is { Length: > 0 } name ? name : "authentik";
        Authority = section["Authority"];
        ClientId = section["ClientId"];
        ClientSecret = section["ClientSecret"];
        SessionHours = section.GetValue("SessionHours", 8);
        RecoveryUserIds = section.GetSection("RecoveryUserIds").Get<Guid[]>() ?? [];
        if (Required && (!Enabled || RecoveryUserIds.Length == 0))
            throw new InvalidOperationException("Required OIDC needs an enabled provider and explicit RecoveryUserIds.");
        if (DisplayName.Length > 80 || SessionHours is < 1 or > 24)
            throw new InvalidOperationException("OIDC display name must be at most 80 characters; SessionHours must be 1–24.");
        if (!Enabled) return;
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("OIDC needs an HTTPS authority, client ID, and client secret (HTTP is allowed only on loopback).");
    }

    public bool AllowsPassword(Guid userId) => !Required || RecoveryUserIds.Contains(userId);

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
