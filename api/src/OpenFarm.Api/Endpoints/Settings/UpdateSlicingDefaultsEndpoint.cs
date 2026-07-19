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
    public string DefaultSlicerEngine { get; set; } = string.Empty;

    // No natural "Id" on this request/response pair — ActivityLogPostProcessor's default path
    // reflects for an "Id" property on Response first, so SlicingDefaultsResponse.Id below (the
    // well-known Organization id) is what actually makes this loggable, not a request-side property.
    public string EntityType => "Organization";

    public string EventType => "SlicingDefaultsUpdated";
}

public sealed class UpdateSlicingDefaultsRequestValidator : AbstractValidator<UpdateSlicingDefaultsRequest>
{
    public UpdateSlicingDefaultsRequestValidator()
    {
        RuleFor(x => x.DefaultSlicerEngine)
            .Must(v => Enum.TryParse<SlicerEngine>(v, ignoreCase: true, out _))
            .WithMessage("DefaultSlicerEngine must be OrcaSlicer or BambuStudio.");
    }
}

// PUT /api/v1/settings/slicing-defaults (contracts/part-library-api.md).
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
        var engine = Enum.Parse<SlicerEngine>(req.DefaultSlicerEngine, ignoreCase: true);
        var result = await handler.HandleAsync(new UpdateSlicingDefaultsCommand(engine), ct);

        Response = new SlicingDefaultsResponse { DefaultSlicerEngine = result.DefaultSlicerEngine };
    }
}
