using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Settings;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed record DisplayPreferencesResponse(string CurrencyCode);
[Permission("auth:self")]
public sealed class DisplayPreferencesEndpoint(IFarmSettingsRepository settings) : EndpointWithoutRequest<DisplayPreferencesResponse>
{
    public override void Configure() { Get("/api/v1/auth/display-preferences"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct) => Response = new((await settings.GetAsync(ct)).CurrencyCode);
}
