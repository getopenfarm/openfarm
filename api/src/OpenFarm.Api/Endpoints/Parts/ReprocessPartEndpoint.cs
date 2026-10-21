using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using Handler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.ReprocessPartCommand, OpenFarm.Application.Parts.AddPartResult>;

namespace OpenFarm.Api.Endpoints.Parts;

/// <summary>Both fields optional: none keeps the part's current slicer, an engine and version moves the part to that exact slicer.</summary>
public sealed class ReprocessPartRequest
{
    public string? SlicerEngine { get; set; }

    public string? SlicerVersion { get; set; }
}

[Permission("parts:replace-file")]
public sealed class ReprocessPartEndpoint(Handler handler) : Endpoint<ReprocessPartRequest, PartProcessingJobSubmittedResponse>
{
    public override void Configure()
    {
        Post("/api/v1/parts/{id}/reprocess");
        Policies("Permission");
    }

    public override async Task HandleAsync(ReprocessPartRequest req, CancellationToken ct)
    {
        SlicerEngine? engine = null;
        if (!string.IsNullOrWhiteSpace(req.SlicerEngine))
        {
            if (!Enum.TryParse<SlicerEngine>(req.SlicerEngine, ignoreCase: true, out var parsed))
            {
                ThrowError("SlicerEngine must be OrcaSlicer, BambuStudio, or SuperSlicer.");
                return;
            }

            engine = parsed;
        }

        if (req.SlicerVersion is not null && engine is null)
        {
            ThrowError("SlicerVersion requires SlicerEngine to also be specified.");
            return;
        }

        _ = Guid.TryParse(HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId);
        var result = await handler.HandleAsync(new ReprocessPartCommand(Route<Guid>("id"), userId, engine, req.SlicerVersion), ct);
        Response = new PartProcessingJobSubmittedResponse { JobId = result.JobId, Status = result.Status.ToString() };
        HttpContext.Response.StatusCode = StatusCodes.Status202Accepted;
    }
}
