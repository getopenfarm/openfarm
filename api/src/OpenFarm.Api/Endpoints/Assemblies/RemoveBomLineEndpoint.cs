using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using RemoveBomLineHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.RemoveBomLineCommand, bool>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class RemoveBomLineRequest : IActivityLoggable
{
    public Guid AssemblyId { get; set; }

    public Guid LineId { get; set; }

    public string EntityType => "Assembly";

    public string EventType => "BomLineRemoved";
}

/// <summary>DELETE /api/v1/assemblies/{id}/lines/{lineId} — 409 if this is the assembly's last line (FR-002).</summary>
[Permission("assemblies:edit")]
public sealed class RemoveBomLineEndpoint(RemoveBomLineHandler handler) : Endpoint<RemoveBomLineRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/assemblies/{id}/lines/{lineId}");
        Policies("Permission");
    }

    public override async Task HandleAsync(RemoveBomLineRequest req, CancellationToken ct)
    {
        req.AssemblyId = Route<Guid>("id");
        req.LineId = Route<Guid>("lineId");

        await handler.HandleAsync(new RemoveBomLineCommand(req.AssemblyId, req.LineId), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
