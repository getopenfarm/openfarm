using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class EffectiveSlicerDefaultResponse
{
    public string Engine { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsExplicit { get; set; }
}

public sealed class AvailableSlicerResponse
{
    public string Engine { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
}

public sealed class SlicingDefaultsResponse
{
    // Reflected by ActivityLogPostProcessor's default single-entity path (UpdateSlicingDefaultsEndpoint) — the well-known singleton Organization this setting always belongs to.
    public Guid Id { get; set; } = Organization.WellKnownId;

    /// <summary>The effective default; <c>null</c> only when <see cref="Available"/> is empty (FR-011).</summary>
    public EffectiveSlicerDefaultResponse? Default { get; set; }

    public EstimationProfilesRequest? EstimationProfiles { get; set; }

    public List<AvailableSlicerResponse> Available { get; set; } = [];

    public static SlicingDefaultsResponse FromResult(SlicingDefaults result) => new()
    {
        EstimationProfiles = result.EstimationProfiles is { } p ? new EstimationProfilesRequest
        {
            MachineProfileId = p.MachineProfileId,
            ProcessProfileId = p.ProcessProfileId,
            FilamentProfileId = p.FilamentProfileId,
            BuildPlateType = p.BuildPlateType.ToString()
        } : null,
        Default = result.Default is { } d
            ? new EffectiveSlicerDefaultResponse { Engine = d.Engine.ToString(), Version = d.Version, IsExplicit = d.IsExplicit }
            : null,
        Available = result.Available
            .Select(a => new AvailableSlicerResponse { Engine = a.Engine.ToString(), Version = a.Version, Platform = a.Platform.ToString() })
            .ToList(),
    };
}

// GET /api/v1/settings/slicing-defaults (contracts/slicer-management-api.md).
[Permission("settings:slicing-defaults:read")]
public sealed class GetSlicingDefaultsEndpoint(IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults> handler)
    : EndpointWithoutRequest<SlicingDefaultsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/settings/slicing-defaults");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetSlicingDefaultsQuery(), ct);
        Response = SlicingDefaultsResponse.FromResult(result);
    }
}
