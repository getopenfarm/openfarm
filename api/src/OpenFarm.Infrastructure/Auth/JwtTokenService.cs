using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace OpenFarm.Infrastructure.Auth;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>The access token of a connected app, read back for revocation.</summary>
public sealed record AppTokenClaims(Guid UserId, Guid SessionId, string ClientId);

public sealed class JwtTokenService(IConfiguration configuration)
{
    public const string ClientIdClaim = "client_id";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]
        ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.")));

    public IssuedToken IssueToken(Guid userId, Guid sessionId) => Write(
        configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured."),
        configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured."),
        [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim("sid", sessionId.ToString())]);

    /// <summary>
    /// A connected app's access token: issuer <c>ORIGIN</c>, audience <c>ORIGIN/mcp</c>, so it never passes the
    /// browser scheme, and its <c>sid</c> names an app session, which the browser session check refuses.
    /// </summary>
    public IssuedToken IssueAppToken(Guid userId, Guid sessionId, string clientId, string issuer, string audience) => Write(issuer, audience,
        [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim("sid", sessionId.ToString()), new Claim(ClientIdClaim, clientId)]);

    /// <summary>A validly signed app token for this issuer and audience, even when expired; otherwise null.</summary>
    public AppTokenClaims? ReadAppToken(string token, string issuer, string audience)
    {
        if (token.Length > 4096) return null;
        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = Key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateLifetime = false,
            }, out _);
            return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
                && Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId)
                && principal.FindFirst(ClientIdClaim)?.Value is { } clientId
                    ? new AppTokenClaims(userId, sessionId, clientId)
                    : null;
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private IssuedToken Write(string issuer, string audience, Claim[] claims)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(Lifetime);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: expiresAtUtc.UtcDateTime,
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
