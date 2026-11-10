namespace OpenFarm.Infrastructure.Auth;

/// <summary>
/// OAuth sign-in for MCP clients: off unless <c>OAUTH_ENABLED=true</c>, and then bound to <c>ORIGIN</c>.
/// Issuer and resource come only from this validated setting, never from request Host or forwarded headers.
/// </summary>
public sealed class OAuthSettings
{
    public bool Enabled { get; }

    /// <summary>The issuer, <c>ORIGIN</c>. Null when OAuth is off.</summary>
    public string? Issuer { get; }

    /// <summary>The protected resource and access-token audience, <c>ORIGIN/mcp</c>. Null when OAuth is off.</summary>
    public string? Resource { get; }

    public string? ResourceMetadataUrl { get; }

    /// <param name="enabled">The raw <c>OAUTH_ENABLED</c> value.</param>
    /// <param name="origin">The normalized <c>ORIGIN</c> (no trailing slash), or null.</param>
    public OAuthSettings(string? enabled, string? origin)
    {
        if (string.IsNullOrWhiteSpace(enabled)) return;
        if (!bool.TryParse(enabled.Trim(), out var on)) throw new InvalidOperationException("OAUTH_ENABLED must be true or false.");
        if (!on) return;
        if (origin is null) throw new InvalidOperationException("OAUTH_ENABLED requires ORIGIN (for example https://farm.example.com).");
        var uri = new Uri(origin, UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
            throw new InvalidOperationException("OAUTH_ENABLED requires an HTTPS ORIGIN. HTTP is allowed only for localhost.");
        Enabled = true;
        Issuer = origin;
        Resource = origin + "/mcp";
        ResourceMetadataUrl = origin + "/.well-known/oauth-protected-resource/mcp";
    }
}
