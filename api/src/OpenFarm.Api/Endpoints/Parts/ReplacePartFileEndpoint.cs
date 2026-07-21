using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using ReplacePartFileHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.ReplacePartFileCommand, OpenFarm.Application.Parts.AddPartResult>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class ReplacePartFileRequest
{
    public IFormFile File { get; set; } = null!;

    public string? SlicerEngine { get; set; }

    public string? SlicerVersion { get; set; }
}

// PUT /api/v1/parts/{id}/file (contracts/part-library-api.md) — same request shape as POST /api/v1/parts.
[Permission("parts:replace-file")]
public sealed class ReplacePartFileEndpoint(ReplacePartFileHandler handler) : Endpoint<ReplacePartFileRequest, PartProcessingJobSubmittedResponse>
{
    private const long MaxFileSizeBytes = 500 * 1024 * 1024;

    public override void Configure()
    {
        Put("/api/v1/parts/{id}/file");
        AllowFileUploads();
        MaxRequestBodySize(MaxFileSizeBytes);
        Policies("Permission");
    }

    public override async Task HandleAsync(ReplacePartFileRequest req, CancellationToken ct)
    {
        var partId = Route<Guid>("id");

        SlicerEngine? slicerEngineOverride = null;
        if (!string.IsNullOrWhiteSpace(req.SlicerEngine))
        {
            if (!Enum.TryParse<SlicerEngine>(req.SlicerEngine, ignoreCase: true, out var parsed))
            {
                ThrowError("SlicerEngine must be OrcaSlicer, BambuStudio, or SuperSlicer.");
                return;
            }

            slicerEngineOverride = parsed;
        }

        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        _ = Guid.TryParse(sub, out var userId);

        await using var stream = req.File.OpenReadStream();
        var command = new ReplacePartFileCommand(partId, stream, req.File.FileName, slicerEngineOverride, req.SlicerVersion, userId);
        var result = await handler.HandleAsync(command, ct);

        Response = new PartProcessingJobSubmittedResponse { JobId = result.JobId, Status = result.Status.ToString() };
        HttpContext.Response.StatusCode = StatusCodes.Status202Accepted;
    }
}
