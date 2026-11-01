using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed record EntityIdResponse(Guid Id);
public sealed record UpdateAccountRequest(Guid Id, string Username, string Email, string? Name, Guid[] GroupIds, string[] ExtraGrants, bool IsEnabled);
public sealed record UpdateGroupRequest(Guid Id, string Name, string Description, string[] Grants);
public sealed record ResetAccountPasswordRequest(Guid Id, string Password);
public sealed record PermissionCatalogResponse(PermissionArea[] Areas, PermissionDefinition[] Permissions);

[Permission("users:manage")]
public sealed class ListUsersEndpoint(AccountAdministrationService service) : EndpointWithoutRequest<AccountResponse[]>
{
    public override void Configure() { Get("/api/v1/admin/users"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        _ = service;
        Response = await service.ListUsersAsync(ct);
        await Task.CompletedTask;
    }
}

[Permission("users:manage")]
public sealed class ListGroupsEndpoint(AccountAdministrationService service) : EndpointWithoutRequest<GroupResponse[]>
{
    public override void Configure() { Get("/api/v1/admin/groups"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        _ = service;
        Response = await service.ListGroupsAsync(ct);
        await Task.CompletedTask;
    }
}

[Permission("users:manage")]
public sealed class PermissionCatalogEndpoint : EndpointWithoutRequest<PermissionCatalogResponse>
{
    public override void Configure() { Get("/api/v1/admin/permissions"); Policies("Permission"); }
    public override Task HandleAsync(CancellationToken ct)
    {
        Response = new([.. PermissionCatalog.Areas], [.. PermissionCatalog.All]);
        return Task.CompletedTask;
    }
}

[Permission("users:manage")]
public sealed class CreateUserEndpoint(AccountAdministrationService service) : Endpoint<AccountInput, EntityIdResponse>
{
    public override void Configure() { Post("/api/v1/admin/users"); Policies("Permission"); }
    public override async Task HandleAsync(AccountInput req, CancellationToken ct)
    {
        try { Response = new(await service.SaveUserAsync(null, req, SessionCookies.UserId(HttpContext), ct)); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class UpdateUserEndpoint(AccountAdministrationService service) : Endpoint<UpdateAccountRequest, EntityIdResponse>
{
    public override void Configure() { Put("/api/v1/admin/users/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(UpdateAccountRequest req, CancellationToken ct)
    {
        try { Response = new(await service.SaveUserAsync(req.Id, new AccountInput(req.Username, req.Email, req.Name, req.GroupIds, req.ExtraGrants, req.IsEnabled, null), SessionCookies.UserId(HttpContext), ct)); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class RemoveUserEndpoint(AccountAdministrationService service) : Endpoint<RevokeSessionRequest>
{
    public override void Configure() { Delete("/api/v1/admin/users/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        try { await service.RemoveUserAsync(req.Id, SessionCookies.UserId(HttpContext), ct); await Send.NoContentAsync(ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class CreateGroupEndpoint(AccountAdministrationService service) : Endpoint<GroupInput, EntityIdResponse>
{
    public override void Configure() { Post("/api/v1/admin/groups"); Policies("Permission"); }
    public override async Task HandleAsync(GroupInput req, CancellationToken ct)
    {
        try { Response = new(await service.SaveGroupAsync(null, req, SessionCookies.UserId(HttpContext), ct)); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class UpdateGroupEndpoint(AccountAdministrationService service) : Endpoint<UpdateGroupRequest, EntityIdResponse>
{
    public override void Configure() { Put("/api/v1/admin/groups/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(UpdateGroupRequest req, CancellationToken ct)
    {
        try { Response = new(await service.SaveGroupAsync(req.Id, new GroupInput(req.Name, req.Description, req.Grants), SessionCookies.UserId(HttpContext), ct)); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class RemoveGroupEndpoint(AccountAdministrationService service) : Endpoint<RevokeSessionRequest>
{
    public override void Configure() { Delete("/api/v1/admin/groups/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(RevokeSessionRequest req, CancellationToken ct)
    {
        try { await service.RemoveGroupAsync(req.Id, SessionCookies.UserId(HttpContext), ct); await Send.NoContentAsync(ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("users:manage")]
public sealed class ResetAccountPasswordEndpoint(AccountAdministrationService service) : Endpoint<ResetAccountPasswordRequest>
{
    public override void Configure() { Post("/api/v1/admin/users/{id}/reset-password"); Policies("Permission"); }
    public override async Task HandleAsync(ResetAccountPasswordRequest req, CancellationToken ct)
    {
        try { await service.ResetPasswordAsync(req.Id, req.Password, SessionCookies.UserId(HttpContext), ct); await Send.NoContentAsync(ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}

[Permission("auth:self")]
public sealed class ChangePasswordEndpoint(AccountAdministrationService service) : Endpoint<PasswordInput>
{
    public override void Configure() { Post("/api/v1/auth/change-password"); Policies("Permission"); }
    public override async Task HandleAsync(PasswordInput req, CancellationToken ct)
    {
        var session = Guid.TryParse(User.FindFirst("sid")?.Value, out var sid) ? sid : (Guid?)null;
        try { await service.ChangePasswordAsync(SessionCookies.UserId(HttpContext), req, session, ct); await Send.NoContentAsync(ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}


[Permission("auth:self")]
public sealed class UpdateProfileEndpoint(AccountAdministrationService service) : Endpoint<ProfileInput>
{
    public override void Configure() { Put("/api/v1/auth/profile"); Policies("Permission"); }
    public override async Task HandleAsync(ProfileInput req, CancellationToken ct)
    {
        try { await service.UpdateProfileAsync(SessionCookies.UserId(HttpContext), req, ct); await Send.NoContentAsync(ct); }
        catch (ArgumentException e) { AddError(e.Message); await Send.ErrorsAsync(cancellation: ct); }
        catch (KeyNotFoundException) { await Send.NotFoundAsync(ct); }
    }
}
