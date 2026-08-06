using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using DeleteNonPrintedComponentHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.DeleteNonPrintedComponentCommand, bool>;

namespace OpenFarm.Api.Endpoints.NonPrintedComponents;

public sealed class DeleteNonPrintedComponentRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "NonPrintedComponent";

    public string EventType => "NonPrintedComponentDeleted";
}

/// <summary>DELETE /api/v1/non-printed-components/{id} — 409 if still referenced by a BOM line (FR-011).</summary>
[Permission("non-printed-components:delete")]
public sealed class DeleteNonPrintedComponentEndpoint(DeleteNonPrintedComponentHandler handler) : Endpoint<DeleteNonPrintedComponentRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/non-printed-components/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(DeleteNonPrintedComponentRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new DeleteNonPrintedComponentCommand(req.Id), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
