using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class ExplodeAssemblyRequest
{
    public Guid Id { get; set; }

    public int Quantity { get; set; }
}

public sealed class ExplodedPartResponse
{
    public Guid PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
}

public sealed class ExplodedNonPrintedComponentResponse
{
    public Guid NonPrintedComponentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
}

public sealed class ExplodeAssemblyResponse
{
    public List<ExplodedPartResponse> PrintedParts { get; set; } = [];

    public List<ExplodedNonPrintedComponentResponse> NonPrintedComponents { get; set; } = [];
}

/// <summary>GET /api/v1/assemblies/{id}/explosion?quantity={n} — FR-009, pure/read-only (contracts/assembly-bom-api.md).</summary>
[Permission("assemblies:view")]
public sealed class ExplodeAssemblyEndpoint(IQueryHandler<ExplodeAssemblyQuery, BomExplosionResult> handler)
    : Endpoint<ExplodeAssemblyRequest, ExplodeAssemblyResponse>
{
    public override void Configure()
    {
        Get("/api/v1/assemblies/{id}/explosion");
        Policies("Permission");
    }

    public override async Task HandleAsync(ExplodeAssemblyRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var result = await handler.HandleAsync(new ExplodeAssemblyQuery(req.Id, req.Quantity), ct);

        Response = new ExplodeAssemblyResponse
        {
            PrintedParts = result.PrintedParts
                .Select(p => new ExplodedPartResponse { PartId = p.PartId, PartName = p.PartName, Quantity = p.Quantity })
                .ToList(),
            NonPrintedComponents = result.NonPrintedComponents
                .Select(c => new ExplodedNonPrintedComponentResponse { NonPrintedComponentId = c.NonPrintedComponentId, Name = c.Name, Quantity = c.Quantity })
                .ToList(),
        };
    }
}
