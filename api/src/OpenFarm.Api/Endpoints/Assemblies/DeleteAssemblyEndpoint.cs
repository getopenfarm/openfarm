using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using DeleteAssemblyHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.DeleteAssemblyCommand, bool>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class DeleteAssemblyRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Assembly";

    public string EventType => "AssemblyDeleted";
}

/// <summary>DELETE /api/v1/assemblies/{id} — 409 if still referenced by another assembly (FR-011); its own lines cascade at the DB layer.</summary>
[Permission("assemblies:delete")]
public sealed class DeleteAssemblyEndpoint(DeleteAssemblyHandler handler) : Endpoint<DeleteAssemblyRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/assemblies/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(DeleteAssemblyRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new DeleteAssemblyCommand(req.Id), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
