using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using DeletePartHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.DeletePartCommand, bool>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class DeletePartRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "Part";

    public string EventType => "PartDeleted";
}

// DELETE /api/v1/parts/{id} — cascades tags/requirements/thumbnails; deletes the stored file (contracts/part-library-api.md).
[Permission("parts:delete")]
public sealed class DeletePartEndpoint(DeletePartHandler handler) : Endpoint<DeletePartRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/parts/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(DeletePartRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new DeletePartCommand(req.Id), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
