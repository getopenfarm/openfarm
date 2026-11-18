using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Security;

public sealed class OidcBackchannelLogoutEndpoint(OidcBackchannelLogout service, OidcSettings settings) : EndpointWithoutRequest
{
    private const int MaxBody = 16384;
    public override void Configure()
    {
        Post("/api/v1/auth/sso/backchannel-logout"); AllowAnonymous(); Throttle(60, 60);
        Options(route => route.ExcludeFromDescription());
    }
    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        if (!settings.Enabled) { await Send.NotFoundAsync(ct); return; }
        if (HttpContext.Request.ContentLength > MaxBody || !HttpContext.Request.HasFormContentType)
        { await Send.StringAsync("Invalid logout request.", 400, cancellation: ct); return; }
        var size = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false }) size.MaxRequestBodySize = MaxBody;
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await HttpContext.Request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxBody + 1 - (int)body.Length)), ct)) > 0)
            {
                body.Write(buffer, 0, read);
                if (body.Length > MaxBody) throw new ArgumentException("Oversized logout request.");
            }
            var form = QueryHelpers.ParseQuery(Encoding.UTF8.GetString(body.ToArray()));
            if (!form.TryGetValue("logout_token", out var tokens) || tokens.Count != 1 || !await service.HandleAsync(tokens[0], ct))
                throw new ArgumentException("Invalid logout token.");
            await Send.OkAsync(cancellation: ct);
        }
        catch (Exception e) when (e is ArgumentException or JsonException or SecurityTokenException or BadHttpRequestException)
        { await Send.StringAsync("Invalid logout request.", 400, cancellation: ct); }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException)
        { await Send.StringAsync("Sign-in provider temporarily unavailable.", 503, cancellation: ct); }
    }
}

public sealed class OidcBackchannelLogout(OpenFarmDbContext db, OidcSettings settings, AuthenticationConfiguration configuration,
    OidcMetadataCache metadata, IOptionsFactory<OpenIdConnectOptions> optionsFactory, TimeProvider time)
{
    private const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";
    public async Task<bool> HandleAsync(string? encoded, CancellationToken ct)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(encoded) || encoded.Length > 12288) return false;
        // Explicit custom managers support isolated providers; ordinary requests share discovery/key rollover state.
        var manager = optionsFactory.Create(OidcAuthentication.Scheme).ConfigurationManager!;
        if (manager is ConfigurationManager<OpenIdConnectConfiguration>) manager = metadata.Get(settings.Authority!);
        var discovery = await manager.GetConfigurationAsync(ct);
        if (discovery.Issuer != settings.Authority) return false;
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(encoded, new TokenValidationParameters
        {
            ValidIssuer = settings.Authority,
            ValidAudience = settings.ClientId,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = false,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            IssuerSigningKeys = discovery.SigningKeys,
            ConfigurationManager = manager as BaseConfigurationManager,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
                SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
                SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512],
        });
        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token) return false;
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
        var root = payload.RootElement;
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (root.EnumerateObject().Any(p => !names.Add(p.Name)) || root.TryGetProperty("nonce", out _)
            || !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Object
            || !events.TryGetProperty(LogoutEvent, out var logout) || logout.ValueKind != JsonValueKind.Object
            || events.EnumerateObject().Count(p => p.Name == LogoutEvent) != 1
            || !root.TryGetProperty("iat", out var iat) || iat.ValueKind != JsonValueKind.Number || !iat.TryGetInt64(out var seconds)) return false;
        var now = time.GetUtcNow();
        if (seconds < now.AddMinutes(-5).ToUnixTimeSeconds() || seconds > now.AddMinutes(1).ToUnixTimeSeconds()) return false;
        string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= 255 } text && !string.IsNullOrWhiteSpace(text) ? text : null;
        var subject = Text("sub"); var sid = Text("sid"); var jti = Text("jti");
        if (jti is null || subject is null && sid is null
            || names.Contains("sub") && subject is null || names.Contains("sid") && sid is null) return false;
        var receiptId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.SessionIdentity + "|" + jti)));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Same lock order as sign-in, provisioning, settings, and account administration.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426499)", ct);
        if (!await configuration.LockProviderAsync(settings.SessionIdentity, ct)) return false;
        // Lock the same users as sign-in before inspecting receipts or creating/revoking sessions.
        var userQuery = subject is not null
            ? db.UserLogins.Where(l => l.LoginProvider == settings.ProviderKey && l.ProviderKey == subject).Select(l => l.UserId)
            : db.BrowserSessions.Where(s => s.OidcConfigurationIdentity == settings.SessionIdentity && s.OidcSessionId == sid).Select(s => s.UserId);
        var users = await userQuery.Distinct().OrderBy(id => id).ToArrayAsync(ct);
        foreach (var id in users)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        if (await db.OidcLogoutReceipts.AnyAsync(r => r.Id == receiptId, ct))
        { await tx.CommitAsync(ct); return true; }
        await db.OidcLogoutReceipts.Where(r => r.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
        var affected = await db.BrowserSessions.Where(s => s.ClientId == null && s.OidcConfigurationIdentity == settings.SessionIdentity
            && s.RevokedAtUtc == null && (subject == null || s.OidcSubject == subject) && (sid == null || s.OidcSessionId == sid))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        db.OidcLogoutReceipts.Add(new()
        {
            Id = receiptId,
            Provider = settings.SessionIdentity,
            Subject = subject,
            SessionId = sid,
            ReceivedAtUtc = now,
            ExpiresAtUtc = now.AddDays(1)
        });
        if (affected > 0)
            db.ActivityLogEntries.Add(ActivityLogEntry.Create("BrowserSession", receiptId, "OidcBackchannelLogout", null, ActorType.System,
                new ActivityLogDetails(ReportedState: JsonSerializer.Serialize(new { RevokedSessionCount = affected }))));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
