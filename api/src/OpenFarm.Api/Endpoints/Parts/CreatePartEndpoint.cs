using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using AddPartHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.AddPartCommand, OpenFarm.Application.Parts.AddPartResult>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class CreatePartRequest
{
    public IFormFile File { get; set; } = null!;

    /// <summary>Optional (FR-004) — defaults to the organization's effective default when omitted. Only meaningful for a file that requires slicing.</summary>
    public string? SlicerEngine { get; set; }

    /// <summary>Optional (FR-004) — engine-only override resolves to that engine's newest available version; both present resolves to that exact pair.</summary>
    public string? SlicerVersion { get; set; }
}

public sealed class PartProcessingJobSubmittedResponse
{
    public Guid JobId { get; set; }

    public string Status { get; set; } = string.Empty;
}

// POST /api/v1/parts — starts processing; does NOT create a Part (contracts/part-library-api.md).
[Permission("parts:create")]
public sealed class CreatePartEndpoint(AddPartHandler handler) : Endpoint<CreatePartRequest, PartProcessingJobSubmittedResponse>
{
    // Generous for real STL/3MF/G-code uploads while bounding worst-case memory from an oversized upload.
    private const long MaxFileSizeBytes = 500 * 1024 * 1024;

    public override void Configure()
    {
        Post("/api/v1/parts");
        AllowFileUploads();
        MaxRequestBodySize(MaxFileSizeBytes);
        Policies("Permission");
    }

    public override async Task HandleAsync(CreatePartRequest req, CancellationToken ct)
    {
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
        var command = new AddPartCommand(stream, req.File.FileName, slicerEngineOverride, req.SlicerVersion, userId);
        var result = await handler.HandleAsync(command, ct);

        Response = new PartProcessingJobSubmittedResponse { JobId = result.JobId, Status = result.Status.ToString() };
        HttpContext.Response.StatusCode = StatusCodes.Status202Accepted;
    }
}
