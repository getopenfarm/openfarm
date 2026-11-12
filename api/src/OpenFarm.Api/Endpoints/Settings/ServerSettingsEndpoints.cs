using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed record UpdatePublicUrlRequest(Guid Revision, string? PublicUrl);
public sealed record UpdateMcpSettingsRequest(Guid Revision, string Mode);

// The admin/settings path additionally requires actual Administrators membership in PermissionAuthorizationHandler.
[Permission("users:manage")]
public sealed class GetServerSettingsEndpoint(ServerSettingsService service) : EndpointWithoutRequest<ServerSettingsResponse>
{
    public override void Configure() { Get("/api/v1/admin/settings/server"); Policies("Permission", "BrowserSession"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = await service.ReadAsync(ct);
}

[Permission("users:manage")]
public sealed class GetMcpSettingsEndpoint(ServerSettingsService service) : EndpointWithoutRequest<ServerSettingsResponse>
{
    public override void Configure() { Get("/api/v1/admin/settings/mcp"); Policies("Permission", "BrowserSession"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = await service.ReadAsync(ct);
}

[Permission("users:manage")]
public sealed class UpdatePublicUrlEndpoint(ServerSettingsService service) : Endpoint<UpdatePublicUrlRequest, ServerSettingsResponse>
{
    public override void Configure() { Put("/api/v1/admin/settings/server"); Policies("Permission", "BrowserSession"); Throttle(10, 60); }
    public override async Task HandleAsync(UpdatePublicUrlRequest req, CancellationToken ct)
    {
        Response = await service.UpdateAsync(SessionCookies.UserId(HttpContext), req.Revision, true, req.PublicUrl, null, ct);
    }
}

[Permission("users:manage")]
public sealed class UpdateMcpSettingsEndpoint(ServerSettingsService service) : Endpoint<UpdateMcpSettingsRequest, ServerSettingsResponse>
{
    public override void Configure() { Put("/api/v1/admin/settings/mcp"); Policies("Permission", "BrowserSession"); Throttle(10, 60); }
    public override async Task HandleAsync(UpdateMcpSettingsRequest req, CancellationToken ct)
    {
        Response = await service.UpdateAsync(SessionCookies.UserId(HttpContext), req.Revision, false, null, req.Mode, ct);
    }
}
