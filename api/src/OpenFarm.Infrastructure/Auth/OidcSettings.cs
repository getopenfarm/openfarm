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
    public Guid[] RecoveryUserIds { get; }
    /// <summary>What every sign-in needs: the subject, and the profile and email claims that provisioning reads.</summary>
    public static readonly string[] BaseScopes = ["openid", "profile", "email"];
    public const int MaxAdditionalScopes = 10;
    /// <summary>Requested at every sign-in: <see cref="BaseScopes"/>, then the configured extras.</summary>
    public string[] Scopes { get; }
    // Bind external identities to the issuer, not to an email or a mutable display name.
    public string ProviderKey => "oidc:" + Hash(Authority!);
    public string SessionIdentity => Hash($"{Authority}|{ClientId}");

    public OidcSettings(AuthenticationSettingsValues value)
    {
        Enabled = value.OidcEnabled;
        InternalAuthenticationEnabled = value.InternalAuthenticationEnabled;
        // Blank by default; the neutral label keeps the sign-in and linking text readable for any provider.
        DisplayName = value.DisplayName.Trim() is { Length: > 0 } name ? name : "SSO";
        Authority = value.Authority;
        ClientId = value.ClientId;
        ClientSecret = value.ClientSecret;
        RecoveryUserIds = value.RecoveryUserIds;
        Scopes = ParseScopes(value.AdditionalScopes);
        if (!InternalAuthenticationEnabled && (!Enabled || RecoveryUserIds.Length == 0))
            throw new InvalidOperationException("Disabling internal authentication requires OIDC and a recovery administrator.");
        if (DisplayName.Length > 80)
            throw new InvalidOperationException("OIDC display name must be at most 80 characters.");
        if (!Enabled) return;
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("OIDC needs an HTTPS authority, client ID, and client secret (HTTP is allowed only on loopback).");
    }

    /// <summary>
    /// The base scopes followed by the extras, without duplicates. Each extra must be an RFC 6749 scope token.
    /// offline_access is refused: OpenFarm asks for no refresh token and keeps no provider token.
    /// Throws <see cref="ArgumentException"/>, unlike the rest of this class, so a settings save shows this message
    /// instead of the generic provider-settings one.
    /// </summary>
    public static string[] ParseScopes(string? additional)
    {
        var extras = (additional ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (extras.Length > MaxAdditionalScopes || extras.Any(s => s.Length > 128 || !s.All(IsScopeCharacter))
            || extras.Contains("offline_access", StringComparer.Ordinal))
            throw new ArgumentException($"Additional scopes: up to {MaxAdditionalScopes} names, separated by spaces. ASCII only, no quotes or backslashes. offline_access is not allowed.");
        return [.. BaseScopes.Concat(extras).Distinct(StringComparer.Ordinal)];
    }

    // scope-token = 1*( %x21 / %x23-5B / %x5D-7E ) (RFC 6749 section 3.3).
    private static bool IsScopeCharacter(char c) => c is '!' or (>= '#' and <= '[') or (>= ']' and <= '~');

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
