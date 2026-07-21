using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using DiscardHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.DiscardPartProcessingJobCommand, bool>;

namespace OpenFarm.Api.Endpoints.PartProcessingJobs;

public sealed class DiscardPartProcessingJobRequest
{
    public Guid Id { get; set; }
}

// DELETE /api/v1/part-processing-jobs/{id}/confirm — the Cancel action (contracts/part-library-api.md).
[Permission("parts:create")]
public sealed class DiscardPartProcessingJobEndpoint(DiscardHandler handler) : Endpoint<DiscardPartProcessingJobRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/part-processing-jobs/{id}/confirm");
        Policies("Permission");
    }

    public override async Task HandleAsync(DiscardPartProcessingJobRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new DiscardPartProcessingJobCommand(req.Id), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
