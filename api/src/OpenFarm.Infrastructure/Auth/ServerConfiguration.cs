using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenFarm.Domain.Settings;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth;

/// <summary>Validated deployment policy. Explicit MCP_ENABLED controls availability; an unset value leaves control in settings.</summary>
public sealed class ServerConfigurationOverrides
{
    public string? PublicUrl { get; }
    public bool? Enabled { get; }
    public McpAccessMode? Mode { get; }
    public bool? LegacyOAuthEnabled { get; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{PublicUrl}|{Mode}|{Enabled}")));

    public ServerConfigurationOverrides(IConfiguration configuration)
    {
        PublicUrl = NormalizeOrigin(configuration["ORIGIN"]);
        Enabled = ParseBoolean(configuration["MCP_ENABLED"], "MCP_ENABLED");
        var mode = configuration["MCP_MODE"];
        if (!string.IsNullOrWhiteSpace(mode))
        {
            if (!Enum.TryParse<McpAccessMode>(mode, true, out var parsed) || !Enum.IsDefined(parsed) || !string.Equals(parsed.ToString(), mode.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MCP_MODE must be Disabled, AccessTokens, or SignIn.");
            Mode = parsed;
        }
        if (Enabled == true && Mode == McpAccessMode.Disabled)
            throw new InvalidOperationException("MCP_ENABLED=true cannot be combined with MCP_MODE=Disabled.");
        LegacyOAuthEnabled = ParseBoolean(configuration["OAUTH_ENABLED"], "OAUTH_ENABLED");
        // Preserve deployment validation, including a typo or an insecure issuer.
        _ = new OAuthSettings(configuration["OAUTH_ENABLED"], PublicUrl);
    }

    private static bool? ParseBoolean(string? raw, string name)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (bool.TryParse(raw.Trim(), out var result)) return result;
        throw new InvalidOperationException($"{name} must be true or false.");
    }

    public ServerConfigurationSnapshot Resolve(ServerSettings saved)
    {
        var origin = PublicUrl ?? saved.PublicUrl;
        var mode = Mode ?? saved.McpMode;
        if (Enabled == false) mode = McpAccessMode.Disabled;
        else if (Enabled == true && mode == McpAccessMode.Disabled) mode = McpAccessMode.AccessTokens;
        var settings = new OAuthSettings(mode == McpAccessMode.SignIn ? "true" : "false", origin);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{saved.Revision:N}|{origin}")));
        return new(origin, mode, identity, settings);
    }

    public static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        const string error = "ORIGIN must be an absolute HTTP or HTTPS origin without credentials, a non-root path, query, or fragment (for example https://farm.example.com).";
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || value.Contains('?') || value.Contains('#') || value.Contains('@') || value.Contains('\\') || value.Any(char.IsWhiteSpace))
            throw new InvalidOperationException(error);
        var pathStart = value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3);
        if (pathStart >= 0 && value[pathStart..] != "/") throw new InvalidOperationException(error);
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

public sealed record ServerConfigurationSnapshot(string? PublicUrl, McpAccessMode McpMode, string Identity, OAuthSettings OAuth)
{
    public bool McpEnabled => McpMode != McpAccessMode.Disabled;
}

/// <summary>Loaded once per request from the database, never cached across processes.</summary>
public sealed class ServerConfiguration(OpenFarmDbContext db, ServerConfigurationOverrides overrides)
{
    public ServerConfigurationSnapshot Current { get; private set; } = null!;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (Current is not null) return;
        var saved = await db.ServerSettings.AsNoTracking().SingleAsync(s => s.Id == ServerSettings.SingletonId, ct);
        Current = overrides.Resolve(saved);
        Current.OAuth.ConfigurationIdentity = Current.Identity;
    }
}
