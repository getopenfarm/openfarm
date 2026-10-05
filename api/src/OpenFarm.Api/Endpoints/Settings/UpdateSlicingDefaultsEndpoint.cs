using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using UpdateSlicingDefaultsHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.UpdateSlicingDefaultsCommand, OpenFarm.Application.Parts.SlicingDefaults>;

namespace OpenFarm.Api.Endpoints.Settings;

public sealed class UpdateSlicingDefaultsRequest : IActivityLoggable
{
    public string Engine { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    private EstimationProfilesRequest? _estimationProfiles;
    /// <summary>Explicit profiles for STL/geometry-only 3MF estimates. Omit to preserve, null to clear.</summary>
    public EstimationProfilesRequest? EstimationProfiles
    {
        get => _estimationProfiles;
        set { _estimationProfiles = value; EstimationProfilesProvided = true; }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EstimationProfilesProvided { get; private set; }

    // No natural "Id" on this request/response pair — ActivityLogPostProcessor's default path
    // reflects for an "Id" property on Response first, so SlicingDefaultsResponse.Id below (the
    // well-known Organization id) is what actually makes this loggable, not a request-side property.
    public string EntityType => "Organization";

    public string EventType => "SlicingDefaultsUpdated";
}

public sealed class EstimationProfilesRequest
{
    public Guid MachineProfileId { get; set; }
    public Guid ProcessProfileId { get; set; }
    public Guid FilamentProfileId { get; set; }
    public string BuildPlateType { get; set; } = string.Empty;
}

public sealed class UpdateSlicingDefaultsRequestValidator : AbstractValidator<UpdateSlicingDefaultsRequest>
{
    public UpdateSlicingDefaultsRequestValidator()
    {
        RuleFor(x => x.Engine)
            .Must(v => Enum.TryParse<SlicerEngine>(v, ignoreCase: true, out _))
            .WithMessage("Engine must be OrcaSlicer, BambuStudio, or SuperSlicer.");

        RuleFor(x => x.Version).NotEmpty();
        When(x => x.EstimationProfiles is not null, () =>
        {
            RuleFor(x => x.EstimationProfiles!.MachineProfileId).NotEmpty();
            RuleFor(x => x.EstimationProfiles!.ProcessProfileId).NotEmpty();
            RuleFor(x => x.EstimationProfiles!.FilamentProfileId).NotEmpty();
            RuleFor(x => x.EstimationProfiles!.BuildPlateType).Must(v =>
                Enum.TryParse<OpenFarm.Domain.Printers.BuildPlateType>(v, true, out var plate) && Enum.IsDefined(plate));
        });
    }
}

// PUT /api/v1/settings/slicing-defaults (contracts/slicer-management-api.md).
[Permission("settings:slicing-defaults:update")]
public sealed class UpdateSlicingDefaultsEndpoint(UpdateSlicingDefaultsHandler handler)
    : Endpoint<UpdateSlicingDefaultsRequest, SlicingDefaultsResponse>
{
    public override void Configure()
    {
        Put("/api/v1/settings/slicing-defaults");
        Policies("Permission");
    }

    public override async Task HandleAsync(UpdateSlicingDefaultsRequest req, CancellationToken ct)
    {
        var engine = Enum.Parse<SlicerEngine>(req.Engine, ignoreCase: true);
        var result = await handler.HandleAsync(new UpdateSlicingDefaultsCommand(engine, req.Version,
            req.EstimationProfiles is { } p ? new EstimationProfileSelection(p.MachineProfileId, p.ProcessProfileId,
                p.FilamentProfileId, Enum.Parse<OpenFarm.Domain.Printers.BuildPlateType>(p.BuildPlateType, true)) : null,
            req.EstimationProfilesProvided), ct);

        Response = SlicingDefaultsResponse.FromResult(result);
    }
}
