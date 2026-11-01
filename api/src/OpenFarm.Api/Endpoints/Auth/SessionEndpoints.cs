using FastEndpoints;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Security;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed record CsrfResponse(string Token);
public sealed class CsrfEndpoint(IAntiforgery antiforgery) : EndpointWithoutRequest<CsrfResponse>
{
    public override void Configure() { Get("/api/v1/auth/csrf"); AllowAnonymous(); }
    public override Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        Response = new(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);
        return Task.CompletedTask;
    }
}

public sealed class RefreshEndpoint(BrowserSessionService sessions) : EndpointWithoutRequest<LoginResponse>
{
    public override void Configure() { Post("/api/v1/auth/refresh"); AllowAnonymous(); Throttle(60, 60); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await SessionCookies.ValidateAsync(HttpContext)) return;
        var result = await sessions.RefreshAsync(HttpContext.Request.Cookies[SessionCookies.RefreshName] ?? string.Empty, ct);
        if (result is null)
        {
            SessionCookies.Clear(HttpContext);
            await HttpContext.Response.SendUnauthorizedAsync(ct);
            return;
        }
        SessionCookies.Set(HttpContext, result);
        Response = new() { Token = result.Token, ExpiresAtUtc = result.ExpiresAtUtc };
    }
}

public sealed class LogoutEndpoint(BrowserSessionService sessions) : EndpointWithoutRequest
{
    public override void Configure() { Post("/api/v1/auth/logout"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await SessionCookies.ValidateAsync(HttpContext)) return;
        await sessions.RevokeCredentialAsync(HttpContext.Request.Cookies[SessionCookies.RefreshName] ?? string.Empty, ct);
        SessionCookies.Clear(HttpContext);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}

public sealed record SessionResponse(Guid Id, string Device, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc, DateTimeOffset ExpiresAtUtc, bool IsCurrent);
[Permission("auth:self")]
public sealed class ListSessionsEndpoint(OpenFarmDbContext db) : EndpointWithoutRequest<SessionResponse[]>
{
    public override void Configure() { Get("/api/v1/auth/sessions"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        Response = await ActiveAsync(db, SessionCookies.UserId(HttpContext), HttpContext, ct);
    }

    internal static Task<SessionResponse[]> ActiveAsync(OpenFarmDbContext db, Guid userId, HttpContext http, CancellationToken ct)
    {
        var current = http.User.FindFirst("sid")?.Value;
        return db.BrowserSessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SessionResponse(s.Id, s.Device, s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.Id.ToString() == current)).ToArrayAsync(ct);
    }
}
public sealed record RevokeSessionRequest(Guid Id);
[Permission("auth:self")]
public sealed class RevokeSessionEndpoint(BrowserSessionService sessions) : Endpoint<RevokeSessionRequest>
{
    public override void Configure() { Delete("/api/v1/auth/sessions/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        var user = SessionCookies.UserId(HttpContext);
        await sessions.RevokeAsync(user, req.Id, user, ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
[Permission("auth:self")]
public sealed class RevokeAllSessionsEndpoint(BrowserSessionService sessions) : EndpointWithoutRequest
{
    public override void Configure() { Post("/api/v1/auth/sessions/revoke-all"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = SessionCookies.UserId(HttpContext);
        await sessions.RevokeAsync(user, null, user, ct);
        SessionCookies.Clear(HttpContext);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}

public sealed record UserSessionRequest(Guid Id, Guid SessionId);

[Permission("users:manage")]
public sealed class ListUserSessionsEndpoint(OpenFarmDbContext db) : Endpoint<RevokeSessionRequest, SessionResponse[]>
{
    public override void Configure() { Get("/api/v1/admin/users/{id}/sessions"); Policies("Permission"); }
    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == req.Id, ct)) { await Send.NotFoundAsync(ct); return; }
        Response = await ListSessionsEndpoint.ActiveAsync(db, req.Id, HttpContext, ct);
    }
}

[Permission("users:manage")]
public sealed class RevokeUserSessionEndpoint(OpenFarmDbContext db, BrowserSessionService sessions) : Endpoint<UserSessionRequest>
{
    public override void Configure() { Delete("/api/v1/admin/users/{id}/sessions/{sessionId}"); Policies("Permission"); }
    public override async Task HandleAsync(UserSessionRequest req, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == req.Id, ct)) { await Send.NotFoundAsync(ct); return; }
        await sessions.RevokeAsync(req.Id, req.SessionId, SessionCookies.UserId(HttpContext), ct);
        await Send.NoContentAsync(ct);
    }
}

[Permission("users:manage")]
public sealed class RevokeUserSessionsEndpoint(OpenFarmDbContext db, BrowserSessionService sessions) : Endpoint<RevokeSessionRequest>
{
    public override void Configure() { Delete("/api/v1/admin/users/{id}/sessions"); Policies("Permission"); }
    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == req.Id, ct)) { await Send.NotFoundAsync(ct); return; }
        var caller = SessionCookies.UserId(HttpContext);
        // On their own account the caller keeps this browser; /api/v1/auth/sessions/revoke-all signs it out too.
        Guid? keep = req.Id == caller && Guid.TryParse(User.FindFirst("sid")?.Value, out var sid) ? sid : null;
        await sessions.RevokeAsync(req.Id, null, caller, keep, ct);
        await Send.NoContentAsync(ct);
    }
}
