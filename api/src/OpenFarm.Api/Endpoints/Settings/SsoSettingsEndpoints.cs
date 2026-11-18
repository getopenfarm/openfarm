using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Endpoints.Settings;

[Permission("users:manage")]
public sealed class GetSsoSettingsEndpoint(SsoSettingsService service) : EndpointWithoutRequest<SsoSettingsResponse>
{
    public override void Configure() { Get("/api/v1/admin/settings/sso"); Policies("Permission", "BrowserSession"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = await service.ReadAsync(ct);
}

[Permission("users:manage")]
public sealed class UpdateSsoSettingsEndpoint(SsoSettingsService service) : Endpoint<UpdateSsoSettingsRequest, SsoSettingsResponse>
{
    public override void Configure() { Put("/api/v1/admin/settings/sso"); Policies("Permission", "BrowserSession"); Throttle(10, 60); }
    public override async Task HandleAsync(UpdateSsoSettingsRequest req, CancellationToken ct) =>
        Response = await service.UpdateAsync(SessionCookies.UserId(HttpContext), Guid.Parse(User.FindFirst("sid")!.Value), req, ct);
}
