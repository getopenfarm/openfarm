using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class DefaultProcessProfileResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class DefaultMachineProfileResponseItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DefaultProcessProfileResponse? DefaultProcessProfile { get; set; }
}

public sealed class FilamentDefaultResponseItem
{
    public string MaterialType { get; set; } = string.Empty;
    public Guid FilamentProfileId { get; set; }
    public string FilamentProfileName { get; set; } = string.Empty;
}

public sealed class SlicingProfileDefaultsResponse
{
    // Null when the organization has no default machine profile configured yet — a valid response
    // state, not an error (see GetSlicingProfileDefaultsQueryHandler's own remarks).
    public DefaultMachineProfileResponseItem? DefaultMachineProfile { get; set; }

    public List<FilamentDefaultResponseItem> FilamentDefaults { get; set; } = [];

    public static SlicingProfileDefaultsResponse FromResult(SlicingProfileDefaults result) => new()
    {
        DefaultMachineProfile = result.DefaultMachineProfile is { } machine
            ? new DefaultMachineProfileResponseItem
            {
                Id = machine.Id,
                Name = machine.Name,
                DefaultProcessProfile = machine.DefaultProcessProfile is { } process
                    ? new DefaultProcessProfileResponse { Id = process.Id, Name = process.Name }
                    : null,
            }
            : null,
        FilamentDefaults = result.FilamentDefaults
            .Select(f => new FilamentDefaultResponseItem
            {
                MaterialType = f.MaterialType,
                FilamentProfileId = f.FilamentProfileId,
                FilamentProfileName = f.FilamentProfileName,
            })
            .ToList(),
    };
}

// GET /api/v1/settings/slicing-profile-defaults — the org's currently configured default machine
// profile (and its paired process profile, if set) plus the material -> filament profile map. Never
// throws for unconfigured state (unlike ResolveSlicingProfileSelection) — an operator needs to see
// "nothing set yet" to know what to configure.
[Permission("settings:slicing-profile-defaults:read")]
public sealed class GetSlicingProfileDefaultsEndpoint(IQueryHandler<GetSlicingProfileDefaultsQuery, SlicingProfileDefaults> handler)
    : EndpointWithoutRequest<SlicingProfileDefaultsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/settings/slicing-profile-defaults");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetSlicingProfileDefaultsQuery(), ct);
        Response = SlicingProfileDefaultsResponse.FromResult(result);
    }
}
