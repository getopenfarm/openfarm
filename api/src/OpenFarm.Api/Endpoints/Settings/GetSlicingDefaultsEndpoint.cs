using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class SlicingDefaultsResponse
{
    // Reflected by ActivityLogPostProcessor's default single-entity path (UpdateSlicingDefaultsEndpoint) — the well-known singleton Organization this setting always belongs to.
    public Guid Id { get; set; } = Organization.WellKnownId;

    public string DefaultSlicerEngine { get; set; } = string.Empty;
}

// GET /api/v1/settings/slicing-defaults (contracts/part-library-api.md).
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
        Response = new SlicingDefaultsResponse { DefaultSlicerEngine = result.DefaultSlicerEngine };
    }
}
