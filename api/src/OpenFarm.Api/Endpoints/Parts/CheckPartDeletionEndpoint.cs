using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class CheckPartDeletionRequest
{
    public List<Guid> PartIds { get; set; } = [];
}

public sealed class CheckPartDeletionResponse
{
    public IReadOnlyList<PartDeletionEligibility> Parts { get; set; } = [];
}

[Permission("parts:delete")]
public sealed class CheckPartDeletionEndpoint(IPartDeletionEligibilityReader reader)
    : Endpoint<CheckPartDeletionRequest, CheckPartDeletionResponse>
{
    public override void Configure()
    {
        Post("/api/v1/parts/deletion-check");
        Policies("Permission");
    }

    public override async Task HandleAsync(CheckPartDeletionRequest req, CancellationToken ct)
    {
        if (req.PartIds is null || req.PartIds.Count is < 1 or > 200 || req.PartIds.Contains(Guid.Empty)
            || req.PartIds.Distinct().Count() != req.PartIds.Count)
        {
            AddError("Provide 1–200 unique, non-empty part IDs.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        Response = new CheckPartDeletionResponse { Parts = await reader.CheckAsync(req.PartIds, ct) };
    }
}
