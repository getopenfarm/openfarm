using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

/// <summary>
/// <c>/mcp</c> authentication: policy scheme <see cref="Scheme"/> sends <c>ofp_</c> tokens to the PAT handler and
/// everything else to <see cref="OAuthScheme"/>, a JwtBearer that accepts only app access tokens. The browser
/// JwtBearer is never consulted, and this is the one place that writes the MCP challenge.
/// </summary>
public static class McpAuthentication
{
    public const string Scheme = "Mcp";
    public const string OAuthScheme = "OAuth";

    public static void Configure(JwtBearerOptions options, OAuthSettings settings, SecurityKey key)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Null when OAuth is off; then OnMessageReceived refuses every token before validation.
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Resource,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var header = context.Request.Headers.Authorization.ToString();
                // An app access JWT only: never a refresh credential or a PAT, and nothing while sign-in is off.
                if (!settings.Enabled || header.Length < 7
                    || header[7..].StartsWith(BrowserSessionService.AppCredentialPrefix, StringComparison.Ordinal)
                    || header[7..].StartsWith(PersonalAccessTokenService.Prefix, StringComparison.Ordinal))
                    context.NoResult();
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
                    || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId)
                    || principal.FindFirst(CredentialClaims.ClientId)?.Value is not { } clientId
                    || await context.HttpContext.RequestServices.GetRequiredService<BrowserSessionService>()
                        .ResolveAppAsync(userId, sessionId, clientId, context.HttpContext.RequestAborted) is not { } grant)
                {
                    context.Fail("App access was revoked.");
                    return;
                }
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaims(grant.Scopes.Select(s => new Claim(CredentialClaims.Scope, s)));
                identity.AddClaim(new Claim(CredentialClaims.AppName, grant.ClientName));
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                WriteChallenge(context.HttpContext, settings);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>401 with the RFC 6750/9728 challenge MCP clients read to learn how to sign in. Never HTML, never a redirect.</summary>
    public static void WriteChallenge(HttpContext http, OAuthSettings settings)
    {
        var challenge = "Bearer realm=\"openfarm-mcp\"";
        if (settings.Enabled) challenge += $", resource_metadata=\"{settings.ResourceMetadataUrl}\"";
        // RFC 6750 §3.1: invalid_token only when a bearer token was actually sent.
        if (http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) challenge += ", error=\"invalid_token\"";
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = challenge;
        http.Response.Headers.CacheControl = "no-store";
    }
}
