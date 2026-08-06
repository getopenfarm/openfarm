using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class CalculateBuildableUnitsRequest
{
    public Guid Id { get; set; }

    public Dictionary<Guid, decimal> GoodQuantities { get; set; } = [];
}

public sealed class BuildableUnitsResponse
{
    public bool IsApplicable { get; set; }

    public int? Value { get; set; }

    public string? NotApplicableReason { get; set; }
}

/// <summary>
/// POST /api/v1/assemblies/{id}/buildable-units — POST (not GET) because the caller-supplied
/// good-quantity map is a request body (contracts/assembly-bom-api.md). Still a pure computation —
/// nothing persisted.
/// </summary>
[Permission("assemblies:view")]
public sealed class CalculateBuildableUnitsEndpoint(IQueryHandler<CalculateBuildableUnitsQuery, BuildableUnitsResult> handler)
    : Endpoint<CalculateBuildableUnitsRequest, BuildableUnitsResponse>
{
    public override void Configure()
    {
        Post("/api/v1/assemblies/{id}/buildable-units");
        Policies("Permission");
    }

    public override async Task HandleAsync(CalculateBuildableUnitsRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var result = await handler.HandleAsync(new CalculateBuildableUnitsQuery(req.Id, req.GoodQuantities), ct);

        Response = new BuildableUnitsResponse
        {
            IsApplicable = result.IsApplicable,
            Value = result.Value,
            NotApplicableReason = result.NotApplicableReason,
        };
    }
}
