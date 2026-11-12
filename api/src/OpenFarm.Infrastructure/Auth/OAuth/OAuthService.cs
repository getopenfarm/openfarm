using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Auth;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Auth.OAuth;

/// <param name="Allowed">Whether the signed-in user holds any of its permissions now.</param>
/// <param name="Preselected">Ticked on load: allowed Read scopes only, never Change or Printer control.</param>
public sealed record ConsentScope(string Scope, string Label, string[] Includes, bool Allowed, bool Preselected);

/// <param name="Tier"><c>Read</c>, <c>Change</c> or <c>PrinterControl</c>, in that order.</param>
public sealed record ConsentTier(string Tier, ConsentScope[] Scopes);

/// <summary>What the consent page shows for one connection request.</summary>
/// <param name="ClientId">The full client ID.</param>
/// <param name="ClientName">Supplied by the client: an untrusted label.</param>
/// <param name="ReturnsTo">Where the answer goes: the redirect URI of this request.</param>
/// <param name="UnverifiedName">True for Loopback and Native redirects: nothing ties the name to a website.</param>
/// <param name="Instance">This OpenFarm's address (<c>ORIGIN</c>).</param>
public sealed record ConsentResponse(string ClientId, string ClientName, ReturnsTo ReturnsTo, bool UnverifiedName, string Instance,
    string Username, DateTimeOffset AccessEndsAtUtc, ConsentTier[] Tiers);

/// <summary>Where to send the browser after a decision: the client's redirect URI plus these query parameters.</summary>
public sealed record ConsentOutcome(string RedirectUri, IReadOnlyList<KeyValuePair<string, string?>> Query);

/// <param name="Id">The app's session id: revoke it through the session revoke endpoints.</param>
/// <param name="ClientIdSuffix">The last four characters of the client ID.</param>
/// <param name="LastUsedAtUtc">Updated at most once a minute.</param>
public sealed record ConnectedAppResponse(Guid Id, string ClientName, string ClientIdSuffix, ReturnsTo ReturnsTo, AccessChip[] Access,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset LastUsedAtUtc);

public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record OAuthClientRegistration(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] string[] RedirectUris,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("client_secret")] string? ClientSecret,
    [property: JsonPropertyName("client_secret_expires_at")] long? ClientSecretExpiresAt,
    [property: JsonPropertyName("grant_types")] string[] GrantTypes,
    [property: JsonPropertyName("response_types")] string[] ResponseTypes,
    [property: JsonPropertyName("scope")] string Scope);

/// <summary>
/// The OAuth 2.1 authorization server for the <c>/mcp</c> audience (dev-notes/MCP.md). A connected app is a
/// <see cref="BrowserSession"/> with a client id, so rotation, reuse revocation and every revocation trigger are
/// the browser session's own. Nothing here logs or audits a token, code, verifier, secret or request payload.
/// </summary>
public sealed partial class OAuthService(OpenFarmDbContext db, OAuthSettings settings, BrowserSessionService sessions,
    PermissionService permissions, JwtTokenService jwt, IDataProtectionProvider protection, TimeProvider time, ILogger<OAuthService> logger)
{
    public const int MaxClients = 1000;
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(120);
    private static readonly string[] AuthMethods = ["none", "client_secret_basic", "client_secret_post"];
    private static readonly JsonDocumentOptions StrictJson = new() { AllowDuplicateProperties = false, MaxDepth = 8 };

    private sealed record RequestPayload(
        [property: JsonPropertyName("rid")] string Rid,
        [property: JsonPropertyName("cid")] string Cid,
        [property: JsonPropertyName("ruri")] string Ruri,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("ch")] string Ch);

    private sealed record CodePayload(
        [property: JsonPropertyName("sid")] Guid Sid,
        [property: JsonPropertyName("cid")] string Cid,
        [property: JsonPropertyName("ruri")] string Ruri,
        [property: JsonPropertyName("ch")] string Ch);

    private readonly ITimeLimitedDataProtector _requests = protection.CreateProtector("OpenFarm.OAuthRequest.v2", settings.ConfigurationIdentity ?? "", settings.Issuer ?? "").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector _codes = protection.CreateProtector("OpenFarm.OAuthCode.v2", settings.ConfigurationIdentity ?? "", settings.Issuer ?? "").ToTimeLimitedDataProtector();

    private string Issuer => settings.Issuer ?? throw new OAuthException("invalid_request", 404);

    // ---- Dynamic client registration (RFC 7591) ----

    public async Task<OAuthClientRegistration> RegisterAsync(string body, CancellationToken ct)
    {
        JsonElement data;
        try
        {
            using var document = JsonDocument.Parse(body, StrictJson);
            data = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new OAuthException("invalid_client_metadata");
        }
        if (data.ValueKind != JsonValueKind.Object) throw new OAuthException("invalid_client_metadata");

        if (Field(data, "redirect_uris") is not { ValueKind: JsonValueKind.Array } uriArray || uriArray.GetArrayLength() is 0 or > 10)
            throw new OAuthException("invalid_redirect_uri");
        var redirectUris = new List<string>();
        foreach (var item in uriArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || OAuthRules.ClassifyRedirect(item.GetString()) is null)
                throw new OAuthException("invalid_redirect_uri");
            redirectUris.Add(item.GetString()!);
        }

        var method = Field(data, "token_endpoint_auth_method") switch
        {
            null => "client_secret_basic",
            { ValueKind: JsonValueKind.String } m when m.GetString() is { } value && AuthMethods.Contains(value) => value,
            _ => throw new OAuthException("invalid_client_metadata"),
        };
        if (Field(data, "grant_types") is { } grants && (grants.ValueKind != JsonValueKind.Array || grants.GetArrayLength() == 0
            || grants.EnumerateArray().Any(g => g.ValueKind != JsonValueKind.String || g.GetString() is not ("authorization_code" or "refresh_token"))))
            throw new OAuthException("invalid_client_metadata");
        if (Field(data, "response_types") is { } responses && (responses.ValueKind != JsonValueKind.Array || responses.GetArrayLength() != 1
            || responses[0].ValueKind != JsonValueKind.String || responses[0].GetString() != "code"))
            throw new OAuthException("invalid_client_metadata");
        // Scope is accepted but never stored: consent decides what an app gets.
        if (Field(data, "scope") is { } scope && scope.ValueKind != JsonValueKind.String) throw new OAuthException("invalid_client_metadata");
        var rawName = Field(data, "client_name") switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } n => n.GetString(),
            _ => throw new OAuthException("invalid_client_metadata"),
        };
        var name = OAuthRules.NormalizeClientName(rawName) ?? throw new OAuthException("invalid_client_metadata");

        var now = time.GetUtcNow();
        var client = new OAuthClient
        {
            Id = OAuthClient.IdPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Name = name,
            RedirectUris = [.. redirectUris],
            AuthMethod = method,
            CreatedAtUtc = now,
        };
        var secret = method == "none" ? null : "ofs_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        client.SecretHash = secret is null ? null : BrowserSessionService.Hash(secret);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Serializes the capacity check across requests and processes.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71426500)", ct);
            if (await db.OAuthClients.CountAsync(ct) >= MaxClients)
            {
                // Open registration cannot hold capacity: reclaim the oldest client no user ever connected.
                // Never one younger than a consent request: someone may be midway through connecting it.
                var reclaimBefore = now.Subtract(RequestLifetime);
                var reclaim = await db.OAuthClients.Where(c => c.CreatedAtUtc < reclaimBefore && !db.BrowserSessions.Any(s => s.ClientId == c.Id))
                    .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct)
                    ?? throw new OAuthException("temporarily_unavailable", 503);
                await db.OAuthClients.Where(c => c.Id == reclaim).ExecuteDeleteAsync(ct);
            }
            db.OAuthClients.Add(client);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var kinds = string.Join(',', redirectUris.Select(u => OAuthRules.ClassifyRedirect(u)).Distinct());
        LogRegistered(logger, client.Id, method, kinds);
        return new(client.Id, now.ToUnixTimeSeconds(), name, client.RedirectUris, method, secret, secret is null ? null : 0,
            ["authorization_code", "refresh_token"], ["code"], string.Join(' ', OAuthScopes.All.Select(s => s.Scope)));
    }

    private static JsonElement? Field(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    // ---- Authorization request ----

    /// <summary>
    /// Validates an authorization request and returns the signed, 10-minute consent request. Any failure is an
    /// <see cref="OAuthException"/> that the caller shows on OpenFarm, never sent to the client's redirect URI.
    /// </summary>
    public async Task<string> BeginAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        if (p.GetValueOrDefault("response_type") != "code") throw new OAuthException("unsupported_response_type");
        var client = p.GetValueOrDefault("client_id") is { } clientId
            ? await db.OAuthClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clientId, ct)
            : null;
        if (client is null) throw new OAuthException("invalid_client");
        var redirect = p.GetValueOrDefault("redirect_uri");
        if (redirect is null || !client.RedirectUris.Any(r => OAuthRules.RedirectMatches(r, redirect))) throw new OAuthException("invalid_request");
        if (p.TryGetValue("resource", out var resource) && resource != settings.Resource) throw new OAuthException("invalid_target");
        var challenge = p.GetValueOrDefault("code_challenge");
        if (p.GetValueOrDefault("code_challenge_method") != "S256" || !OAuthRules.IsValidChallenge(challenge)) throw new OAuthException("invalid_request");
        var state = p.GetValueOrDefault("state");
        if (!OAuthRules.ValidState(state)) throw new OAuthException("invalid_request");
        // One response mode only; no client parameter can skip consent.
        if (p.TryGetValue("response_mode", out var mode) && mode != "query") throw new OAuthException("invalid_request");
        var payload = new RequestPayload(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), client.Id, redirect, state, challenge!);
        return _requests.Protect(JsonSerializer.Serialize(payload), time.GetUtcNow().Add(RequestLifetime));
    }

    // ---- Consent (browser session only) ----

    public async Task<ConsentResponse> DescribeAsync(string? request, Guid userId, CancellationToken ct)
    {
        var (payload, client) = await LoadRequestAsync(request, ct);
        var username = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName!).SingleAsync(ct);
        var held = await HeldAsync(userId, ct);
        var returnsTo = OAuthRules.ReturnsTo(payload.Ruri);
        var tiers = OAuthScopes.All.GroupBy(s => s.Tier).Select(g => new ConsentTier(g.Key.ToString(), g.Select(s =>
        {
            var allowed = OAuthScopes.Grantable(s.Scope, held);
            return new ConsentScope(s.Scope, s.Label, OAuthScopes.Includes(s), allowed, allowed && s.Tier == ScopeTier.Read);
        }).ToArray())).ToArray();
        return new(client.Id, client.Name, returnsTo, returnsTo.Kind != nameof(RedirectKind.Web), Issuer, username,
            time.GetUtcNow().Add(BrowserSessionService.AppLifetime), tiers);
    }

    public async Task<ConsentOutcome> DecideAsync(string? request, Guid userId, bool approve, string[]? scopes, CancellationToken ct)
    {
        var (payload, client) = await LoadRequestAsync(request, ct);
        var sessionId = SessionIdOf(payload);
        if (!approve)
        {
            // A denied request is used up: a revoked row holds its id, so it cannot be approved afterwards.
            await sessions.CreatePendingAppAsync(userId, client, [], payload.Ruri, sessionId, ct, revoked: true);
            return Outcome(payload, ("error", "access_denied"));
        }
        var held = await HeldAsync(userId, ct);
        var selected = (scopes ?? []).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0 || selected.Any(s => !OAuthScopes.Grantable(s, held))) throw new OAuthException("invalid_scope");
        await sessions.CreatePendingAppAsync(userId, client, [.. OAuthScopes.All.Where(s => selected.Contains(s.Scope)).Select(s => s.Scope)],
            payload.Ruri, sessionId, ct);
        var code = _codes.Protect(JsonSerializer.Serialize(new CodePayload(sessionId, client.Id, payload.Ruri, payload.Ch)), time.GetUtcNow().Add(CodeLifetime));
        return Outcome(payload, ("code", code));
    }

    private ConsentOutcome Outcome(RequestPayload payload, (string Key, string Value) result)
    {
        var query = new List<KeyValuePair<string, string?>> { new(result.Key, result.Value) };
        if (payload.State is not null) query.Add(new("state", payload.State));
        query.Add(new("iss", Issuer)); // RFC 9207: the client checks who answered.
        return new(payload.Ruri, query);
    }

    private async Task<IReadOnlySet<string>> HeldAsync(Guid userId, CancellationToken ct) =>
        (await permissions.ResolveAsync(userId, ct)).Permissions.ToHashSet(StringComparer.Ordinal);

    /// <summary>The pending session id is derived from the request, so each request is approved at most once.</summary>
    private static Guid SessionIdOf(RequestPayload payload) => new(Base64Url.DecodeFromChars(payload.Rid));

    private async Task<(RequestPayload Payload, OAuthClient Client)> LoadRequestAsync(string? request, CancellationToken ct)
    {
        RequestPayload? payload = null;
        // A 2048-character redirect and a 1024-character state make a payload of about 4.5 KB.
        if (request is { Length: > 0 and <= 8192 })
        {
            try
            {
                payload = JsonSerializer.Deserialize<RequestPayload>(_requests.Unprotect(request, out _));
            }
            catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
            {
            }
        }
        if (payload is null) throw new OAuthException("invalid_request");
        var client = await db.OAuthClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == payload.Cid, ct);
        if (client is null || !client.RedirectUris.Any(r => OAuthRules.RedirectMatches(r, payload.Ruri))) throw new OAuthException("invalid_request");
        var sessionId = SessionIdOf(payload);
        if (await db.BrowserSessions.AnyAsync(s => s.Id == sessionId, ct)) throw new OAuthException("invalid_request");
        return (payload, client);
    }

    // ---- Token endpoint ----

    public async Task<OAuthTokenResponse> ExchangeAsync(IReadOnlyDictionary<string, string> form, string? authorization, CancellationToken ct)
    {
        var grantType = form.GetValueOrDefault("grant_type");
        if (grantType is not ("authorization_code" or "refresh_token")) throw new OAuthException("unsupported_grant_type");
        var client = await AuthenticateClientAsync(form, authorization, ct);
        if (form.TryGetValue("resource", out var resource) && resource != settings.Resource) throw new OAuthException("invalid_target");
        return grantType == "authorization_code" ? await ExchangeCodeAsync(form, client, ct) : await RefreshAsync(form, client, ct);
    }

    private async Task<OAuthTokenResponse> ExchangeCodeAsync(IReadOnlyDictionary<string, string> form, OAuthClient client, CancellationToken ct)
    {
        CodePayload? code = null;
        if (form.GetValueOrDefault("code") is { Length: <= 4096 } raw)
        {
            try
            {
                code = JsonSerializer.Deserialize<CodePayload>(_codes.Unprotect(raw, out _));
            }
            catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
            {
            }
        }
        if (code is null || code.Cid != client.Id || form.GetValueOrDefault("redirect_uri") != code.Ruri) throw new OAuthException("invalid_grant");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await db.BrowserSessions.FromSqlInterpolated($"SELECT * FROM \"BrowserSessions\" WHERE \"Id\" = {code.Sid} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (session is null || session.ClientId != client.Id || session.OAuthConfigurationIdentity != settings.ConfigurationIdentity) throw new OAuthException("invalid_grant");
        var verified = OAuthRules.VerifyPkce(form.GetValueOrDefault("code_verifier"), code.Ch);
        if (await db.SessionCredentials.AnyAsync(c => c.SessionId == session.Id, ct))
        {
            // Only a replay that proves the verifier revokes: the code alone (it travels in a URL) changes nothing.
            if (!verified) throw new OAuthException("invalid_grant");
            // RFC 6749 §4.1.2: a code used twice revokes what it issued, the first exchange's tokens included.
            await sessions.RevokeAppAsync(session.Id, "code-reuse", ct);
            await transaction.CommitAsync(ct);
            throw new OAuthException("invalid_grant");
        }
        var now = time.GetUtcNow();
        if (session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now || !await db.Users.AnyAsync(u => u.Id == session.UserId && u.IsEnabled, ct))
            throw new OAuthException("invalid_grant");
        if (!verified)
        {
            // A failed attempt burns the code; the revoked row keeps the request from being approved again.
            session.RevokedAtUtc = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw new OAuthException("invalid_grant");
        }

        var credential = BrowserSessionService.NewAppCredential();
        db.SessionCredentials.Add(new SessionCredential { Hash = BrowserSessionService.Hash(credential), SessionId = session.Id });
        session.LastSeenAtUtc = now;
        await db.SaveChangesAsync(ct);
        // Connecting the same client again replaces its earlier connection. Only exchanged apps: a parallel
        // pending one is not a connection yet, and is never locked here.
        var older = await db.BrowserSessions.Where(s => s.UserId == session.UserId && s.ClientId == client.Id && s.Id != session.Id
                && s.RevokedAtUtc == null && db.SessionCredentials.Any(c => c.SessionId == s.Id))
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in older) await sessions.RevokeAppAsync(id, "replaced", ct);
        await sessions.AuditAppAsync(session.UserId, session, "ConnectedAppAdded", null, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Token(sessions.Issue(session, credential), session.Scopes!);
    }

    private async Task<OAuthTokenResponse> RefreshAsync(IReadOnlyDictionary<string, string> form, OAuthClient client, CancellationToken ct)
    {
        var token = form.GetValueOrDefault("refresh_token");
        if (token is null || !BrowserSessionService.IsAppCredential(token)) throw new OAuthException("invalid_grant");
        var hash = BrowserSessionService.Hash(token);
        var granted = await (from c in db.SessionCredentials
                             join s in db.BrowserSessions on c.SessionId equals s.Id
                             where c.Hash == hash && s.ClientId == client.Id
                             select s.Scopes).SingleOrDefaultAsync(ct);
        if (granted is null) throw new OAuthException("invalid_grant");
        // ponytail: no per-token narrowing; a narrower request still gets the full grant. Store scopes per credential if a client relies on it.
        if (form.TryGetValue("scope", out var scope) && OAuthScopes.Parse(scope).Except(granted, StringComparer.Ordinal).Any())
            throw new OAuthException("invalid_scope");
        var result = await sessions.RefreshAsync(token, client.Id, ct) ?? throw new OAuthException("invalid_grant");
        return Token(result, granted);
    }

    private static OAuthTokenResponse Token(LoginResult result, string[] scopes) =>
        new(result.Token, "Bearer", (int)JwtTokenService.Lifetime.TotalSeconds, result.RefreshToken!, string.Join(' ', scopes));

    // ---- Revocation (RFC 7009) ----

    /// <summary>Revokes the app behind a refresh or access token of the authenticated client. Anything else is a no-op.</summary>
    public async Task RevokeAsync(IReadOnlyDictionary<string, string> form, string? authorization, CancellationToken ct)
    {
        var client = await AuthenticateClientAsync(form, authorization, ct);
        var token = form.GetValueOrDefault("token") ?? throw new OAuthException("invalid_request");
        Guid? sessionId = null;
        if (BrowserSessionService.IsAppCredential(token))
        {
            var hash = BrowserSessionService.Hash(token);
            sessionId = await (from c in db.SessionCredentials
                               join s in db.BrowserSessions on c.SessionId equals s.Id
                               where c.Hash == hash && s.ClientId == client.Id
                               select (Guid?)s.Id).SingleOrDefaultAsync(ct);
        }
        else if (jwt.ReadAppToken(token, Issuer, settings.Resource!) is { } claims && claims.ClientId == client.Id)
        {
            sessionId = await db.BrowserSessions.Where(s => s.Id == claims.SessionId && s.UserId == claims.UserId && s.ClientId == client.Id)
                .Select(s => (Guid?)s.Id).SingleOrDefaultAsync(ct);
        }
        if (sessionId is { } id) await sessions.RevokeAppAsync(id, "app", ct);
    }

    /// <summary>
    /// Client authentication as registered: HTTP Basic, a body secret, or none (public client). A mismatch, a
    /// secret in both places or a body client id other than Basic's fails with <c>invalid_client</c> (401).
    /// </summary>
    private async Task<OAuthClient> AuthenticateClientAsync(IReadOnlyDictionary<string, string> form, string? authorization, CancellationToken ct)
    {
        string? id, secret, method;
        if (!string.IsNullOrEmpty(authorization))
        {
            if (!TryParseBasic(authorization, out id, out secret) || form.ContainsKey("client_secret")
                || (form.TryGetValue("client_id", out var bodyId) && bodyId != id))
                throw new OAuthException("invalid_client", 401);
            method = "client_secret_basic";
        }
        else
        {
            id = form.GetValueOrDefault("client_id");
            secret = form.GetValueOrDefault("client_secret");
            method = secret is null ? "none" : "client_secret_post";
        }
        var client = id is null ? null : await db.OAuthClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        var valid = client is not null && client.AuthMethod == method && (method == "none"
            ? secret is null
            : client.SecretHash is not null && secret is not null && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(BrowserSessionService.Hash(secret)), Encoding.ASCII.GetBytes(client.SecretHash)));
        return valid ? client! : throw new OAuthException("invalid_client", 401);
    }

    private static bool TryParseBasic(string header, out string? id, out string? secret)
    {
        id = secret = null;
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var colon = decoded.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) return false;
            // RFC 6749 §2.3.1: both parts are form-urlencoded before Basic encoding.
            id = Uri.UnescapeDataString(decoded[..colon].Replace('+', ' '));
            secret = Uri.UnescapeDataString(decoded[(colon + 1)..].Replace('+', ' '));
            return secret.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ---- Lists and cleanup ----

    public async Task<ConnectedAppResponse[]> ListAppsAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var rows = await (from s in db.BrowserSessions.AsNoTracking()
                          join c in db.OAuthClients on s.ClientId equals c.Id
                          where s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now
                              && db.SessionCredentials.Any(x => x.SessionId == s.Id)
                          orderby s.CreatedAtUtc descending
                          select new { Session = s, Redirect = s.RedirectUri ?? c.RedirectUris[0] }).ToListAsync(ct);
        return rows.Select(r => new ConnectedAppResponse(r.Session.Id, r.Session.Device, r.Session.ClientId![^4..], OAuthRules.ReturnsTo(r.Redirect),
            OAuthScopes.Chips(r.Session.Scopes ?? []), r.Session.CreatedAtUtc, r.Session.ExpiresAtUtc, r.Session.LastSeenAtUtc)).ToArray();
    }

    /// <summary>Apps never exchanged within 10 minutes, and clients no user connected within 24 hours.</summary>
    public async Task CleanupAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var pendingBefore = now.Subtract(RequestLifetime);
        await db.BrowserSessions.Where(s => s.ClientId != null && s.CreatedAtUtc < pendingBefore && !db.SessionCredentials.Any(c => c.SessionId == s.Id))
            .ExecuteDeleteAsync(ct);
        var unusedBefore = now.AddDays(-1);
        await db.OAuthClients.Where(c => c.CreatedAtUtc < unusedBefore && !db.BrowserSessions.Any(s => s.ClientId == c.Id)).ExecuteDeleteAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OAuth client {ClientId} registered ({AuthMethod}, {RedirectKinds})")]
    private static partial void LogRegistered(ILogger logger, string clientId, string authMethod, string redirectKinds);
}
