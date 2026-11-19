using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed record LoginProvidersResponse(bool OidcEnabled, string DisplayName, bool InternalAuthenticationEnabled);
public sealed class LoginProvidersEndpoint(OidcSettings settings) : EndpointWithoutRequest<LoginProvidersResponse>
{
    public override void Configure() { Get("/api/v1/auth/providers"); AllowAnonymous(); }
    public override Task HandleAsync(CancellationToken ct)
    {
        Response = new(settings.Enabled, settings.DisplayName, settings.InternalAuthenticationEnabled);
        return Task.CompletedTask;
    }
}

public sealed record OidcIdentityResponse(bool Enabled, string DisplayName, bool Linked, bool PasswordAllowed);
[Permission("auth:self")]
public sealed class OidcIdentityEndpoint(OidcSettings settings, OpenFarmDbContext db) : EndpointWithoutRequest<OidcIdentityResponse>
{
    public override void Configure() { Get("/api/v1/auth/sso/identity"); Policies("BrowserSession"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = SessionCookies.UserId(HttpContext);
        Response = new(settings.Enabled, settings.DisplayName,
            settings.Enabled && await db.UserLogins.AnyAsync(l => l.UserId == id && l.LoginProvider == settings.ProviderKey, ct),
            settings.AllowsPassword(id) && !await db.Users.AnyAsync(u => u.Id == id && u.ProvisioningProvider != null, ct));
    }
}

public sealed record BeginOidcLinkRequest(string Password);
public sealed record BeginOidcLinkResponse(string RedirectUri);
[Permission("auth:self")]
public sealed class BeginOidcLinkEndpoint(OidcSettings settings, OidcAccountService accounts) : Endpoint<BeginOidcLinkRequest, BeginOidcLinkResponse>
{
    public override void Configure() { Post("/api/v1/auth/sso/link"); Policies("BrowserSession"); Throttle(5, 60); }
    public override async Task HandleAsync(BeginOidcLinkRequest req, CancellationToken ct)
    {
        if (!settings.Enabled) { await Send.NotFoundAsync(ct); return; }
        if (!await SessionCookies.ValidateAsync(HttpContext)) return;
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length > 1024) { AddError("Enter your current local password."); await Send.ErrorsAsync(cancellation: ct); return; }
        try
        {
            var ticket = await accounts.BeginLinkAsync(SessionCookies.UserId(HttpContext), Guid.Parse(User.FindFirst("sid")!.Value),
                req.Password, HttpContext.Request.Cookies[SessionCookies.RefreshName] ?? "", ct);
            if (ticket is null) { AddError("Could not confirm this account. Check your current password and try again."); await Send.ErrorsAsync(cancellation: ct); return; }
            HttpContext.Response.Cookies.Append(OidcAuthentication.LinkCookie, ticket, OidcAuthentication.LinkCookieOptions(HttpContext));
            Response = new("/api/v1/auth/sso/link");
        }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
    }
}

public sealed class StartOidcLoginEndpoint(OidcSettings settings) : EndpointWithoutRequest
{
    public override void Configure() { Get("/api/v1/auth/sso/login"); AllowAnonymous(); Throttle(20, 60); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!settings.Enabled) { await Send.NotFoundAsync(ct); return; }
        var properties = new AuthenticationProperties();
        properties.Items["returnTo"] = OidcAuthentication.LocalReturnTo(HttpContext.Request.Query["returnTo"]);
        await OidcAuthentication.StartChallengeAsync(HttpContext, properties);
        HttpContext.MarkResponseStart();
    }
}

public sealed class StartOidcLinkEndpoint(OidcSettings settings, OidcAccountService accounts) : EndpointWithoutRequest
{
    public override void Configure() { Get("/api/v1/auth/sso/link"); AllowAnonymous(); Throttle(10, 60); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!settings.Enabled) { await Send.NotFoundAsync(ct); return; }
        var protectedTicket = HttpContext.Request.Cookies[OidcAuthentication.LinkCookie];
        var ticket = accounts.ReadTicket(protectedTicket);
        if (ticket is null || !await accounts.MatchesBrowserAsync(ticket.UserId, ticket.SessionId,
            HttpContext.Request.Cookies[SessionCookies.RefreshName] ?? "", ct))
        { await Send.UnauthorizedAsync(ct); return; }
        var properties = new AuthenticationProperties();
        properties.Items["link"] = protectedTicket;
        await OidcAuthentication.StartChallengeAsync(HttpContext, properties);
        HttpContext.MarkResponseStart();
    }
}
