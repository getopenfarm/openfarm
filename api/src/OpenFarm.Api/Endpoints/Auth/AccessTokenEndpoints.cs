using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed record TokenOptionsResponse(PermissionArea[] Areas, PermissionDefinition[] Permissions, string[] Available);
public sealed record AccessTokenRequest(Guid Id);
public sealed record UserAccessTokenRequest(Guid Id, Guid TokenId);

[Permission("auth:self")]
public sealed class TokenOptionsEndpoint(PermissionService permissions) : EndpointWithoutRequest<TokenOptionsResponse>
{
    public override void Configure() { Get("/api/v1/auth/tokens/options"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var access = await permissions.ResolveAsync(SessionCookies.UserId(HttpContext), ct);
        Response = new(PermissionCatalog.Areas.Where(a => !a.AdministratorOnly).ToArray(),
            PersonalAccessTokenService.Catalog, access.Permissions.Intersect(PersonalAccessTokenService.Catalog.Select(p => p.Key), StringComparer.Ordinal).ToArray());
    }
}

[Permission("auth:self")]
public sealed class ListAccessTokensEndpoint(PersonalAccessTokenService tokens) : EndpointWithoutRequest<AccessTokenView[]>
{
    public override void Configure() { Get("/api/v1/auth/tokens"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = await tokens.ListAsync(SessionCookies.UserId(HttpContext), ct);
}

[Permission("auth:self")]
public sealed class CreateAccessTokenEndpoint(PersonalAccessTokenService tokens) : Endpoint<AccessTokenInput, AccessTokenCreated>
{
    public override void Configure() { Post("/api/v1/auth/tokens"); Policies("Permission"); Throttle(20, 60); }
    public override async Task HandleAsync(AccessTokenInput req, CancellationToken ct)
    {
        try { Response = await tokens.CreateAsync(SessionCookies.UserId(HttpContext), req, ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
    }
}

[Permission("auth:self")]
public sealed class RegenerateAccessTokenEndpoint(PersonalAccessTokenService tokens) : Endpoint<AccessTokenRequest, AccessTokenCreated>
{
    public override void Configure() { Post("/api/v1/auth/tokens/{id}/regenerate"); Policies("Permission"); Throttle(20, 60); }
    public override async Task HandleAsync(AccessTokenRequest req, CancellationToken ct)
    {
        try { Response = await tokens.RegenerateAsync(SessionCookies.UserId(HttpContext), req.Id, ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("auth:self")]
public sealed class RevokeAccessTokenEndpoint(PersonalAccessTokenService tokens) : Endpoint<AccessTokenRequest>
{
    public override void Configure() { Delete("/api/v1/auth/tokens/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(AccessTokenRequest req, CancellationToken ct)
    {
        var owner = SessionCookies.UserId(HttpContext);
        try { await tokens.RevokeAsync(owner, req.Id, owner, ct); await Send.NoContentAsync(ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("auth:self")]
public sealed class RevokeAllAccessTokensEndpoint(PersonalAccessTokenService tokens) : EndpointWithoutRequest
{
    public override void Configure() { Post("/api/v1/auth/tokens/revoke-all"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var owner = SessionCookies.UserId(HttpContext);
        await tokens.RevokeAsync(owner, null, owner, ct);
        await Send.NoContentAsync(ct);
    }
}

[Permission("users:manage")]
public sealed class ListUserAccessTokensEndpoint(PersonalAccessTokenService tokens, OpenFarmDbContext db) : Endpoint<AccessTokenRequest, AccessTokenView[]>
{
    public override void Configure() { Get("/api/v1/admin/users/{id}/tokens"); Policies("Permission"); }
    public override async Task HandleAsync(AccessTokenRequest req, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == req.Id, ct)) { await Send.NotFoundAsync(ct); return; }
        Response = await tokens.ListAsync(req.Id, ct);
    }
}

[Permission("users:manage")]
public sealed class RevokeUserAccessTokenEndpoint(PersonalAccessTokenService tokens) : Endpoint<UserAccessTokenRequest>
{
    public override void Configure() { Delete("/api/v1/admin/users/{id}/tokens/{tokenId}"); Policies("Permission"); }
    public override async Task HandleAsync(UserAccessTokenRequest req, CancellationToken ct)
    {
        try { await tokens.RevokeAsync(req.Id, req.TokenId, SessionCookies.UserId(HttpContext), ct); await Send.NoContentAsync(ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class RevokeUserAccessTokensEndpoint(PersonalAccessTokenService tokens, OpenFarmDbContext db) : Endpoint<AccessTokenRequest>
{
    public override void Configure() { Delete("/api/v1/admin/users/{id}/tokens"); Policies("Permission"); }
    public override async Task HandleAsync(AccessTokenRequest req, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == req.Id, ct)) { await Send.NotFoundAsync(ct); return; }
        await tokens.RevokeAsync(req.Id, null, SessionCookies.UserId(HttpContext), ct);
        await Send.NoContentAsync(ct);
    }
}
