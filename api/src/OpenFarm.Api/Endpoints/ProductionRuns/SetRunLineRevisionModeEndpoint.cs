using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Domain.Production;
using SetRunLineRevisionModeHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Production.SetRunLineRevisionModeCommand, OpenFarm.Domain.Production.RunLine>;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class SetRunLineRevisionModeRequest : IActivityLoggable
{
    public Guid LineId { get; set; }

    public string RevisionMode { get; set; } = string.Empty;

    public string EntityType => "RunLine";

    public string EventType => "RunLineRevisionModeChanged";
}

public sealed class RunLineRevisionModeResponse
{
    public Guid Id { get; set; }

    public string RevisionMode { get; set; } = string.Empty;
}

/// <summary>
/// Deprecated compatibility endpoint: accepts only the quantity-derived revision mode.
/// Finite lines use Snapshot and unlimited lines use FollowLatest; conflicts return 422.
/// </summary>
[Permission("production-runs:edit")]
public sealed class SetRunLineRevisionModeEndpoint(SetRunLineRevisionModeHandler handler)
    : Endpoint<SetRunLineRevisionModeRequest, RunLineRevisionModeResponse>
{
    public override void Configure()
    {
        Put("/api/v1/production-runs/{runId}/lines/{lineId}/revision-mode");
        Policies("Permission");
    }

    public override async Task HandleAsync(SetRunLineRevisionModeRequest req, CancellationToken ct)
    {
        req.LineId = Route<Guid>("lineId");
        var line = await handler.HandleAsync(
            new OpenFarm.Application.Production.SetRunLineRevisionModeCommand(req.LineId, Enum.Parse<RevisionMode>(req.RevisionMode), Route<Guid>("runId")), ct);

        Response = new RunLineRevisionModeResponse
        {
            Id = line.Id,
            RevisionMode = line.RevisionMode.ToString(),
        };
    }
}

public sealed class SetRunLineRevisionModeValidator : Validator<SetRunLineRevisionModeRequest>
{
    public SetRunLineRevisionModeValidator() => RuleFor(x => x.RevisionMode).Must(x => Enum.GetNames<RevisionMode>().Contains(x));
}
