using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Endpoints.Settings;

[Permission("users:manage")]
public sealed class GetAuthenticationSettingsEndpoint(AuthenticationSettingsService service) : EndpointWithoutRequest<AuthenticationSettingsResponse>
{
    public override void Configure() { Get("/api/v1/admin/settings/authentication"); Policies("Permission", "BrowserSession"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = await service.ReadAsync(ct);
}

[Permission("users:manage")]
public sealed class UpdateAuthenticationSettingsEndpoint(AuthenticationSettingsService service) : Endpoint<UpdateAuthenticationSettingsRequest, AuthenticationSettingsResponse>
{
    public override void Configure() { Put("/api/v1/admin/settings/authentication"); Policies("Permission", "BrowserSession"); Throttle(10, 60); }
    public override async Task HandleAsync(UpdateAuthenticationSettingsRequest req, CancellationToken ct) =>
        Response = await service.UpdateAsync(SessionCookies.UserId(HttpContext), Guid.Parse(User.FindFirst("sid")!.Value), req, ct);
}
