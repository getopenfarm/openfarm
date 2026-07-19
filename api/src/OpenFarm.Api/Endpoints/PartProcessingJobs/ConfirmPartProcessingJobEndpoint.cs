using System.IdentityModel.Tokens.Jwt;
using FastEndpoints;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using ConfirmHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.ConfirmPartProcessingJobCommand, OpenFarm.Domain.Parts.Part>;

namespace OpenFarm.Api.Endpoints.PartProcessingJobs;

public sealed class FilamentRequirementOverrideRequest
{
    public int RequirementIndex { get; set; }
    public Guid? NarrowFilamentVariantId { get; set; }
    public Guid? NarrowFilamentProfileId { get; set; }
}

public sealed class ConfirmPartProcessingJobRequest : IActivityLoggable
{
    /// <summary>Required for an Add-kind job; must be omitted for a Replace-kind job (data-model.md).</summary>
    public string? Name { get; set; }

    public List<string>? Tags { get; set; }

    public List<FilamentRequirementOverrideRequest>? FilamentRequirementOverrides { get; set; }

    public string EntityType => "Part";

    public string EventType => "PartConfirmed";
}

// POST /api/v1/part-processing-jobs/{id}/confirm — the Save action (contracts/part-library-api.md).
[Permission("parts:create")]
public sealed class ConfirmPartProcessingJobEndpoint(ConfirmHandler handler) : Endpoint<ConfirmPartProcessingJobRequest, PartResponse>
{
    public override void Configure()
    {
        Post("/api/v1/part-processing-jobs/{id}/confirm");
        Policies("Permission");
    }

    public override async Task HandleAsync(ConfirmPartProcessingJobRequest req, CancellationToken ct)
    {
        var jobId = Route<Guid>("id");
        var sub = HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        Guid.TryParse(sub, out var userId);

        var overrides = req.FilamentRequirementOverrides?
            .Select(o => new FilamentRequirementOverride(o.RequirementIndex, o.NarrowFilamentVariantId, o.NarrowFilamentProfileId))
            .ToList();

        var command = new ConfirmPartProcessingJobCommand(jobId, req.Name, req.Tags, overrides, userId);
        var part = await handler.HandleAsync(command, ct);

        Response = PartResponse.FromDomain(part);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
