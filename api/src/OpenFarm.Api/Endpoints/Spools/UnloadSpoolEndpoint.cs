using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using UnloadSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.UnloadSpoolCommand, OpenFarm.Domain.FilamentTracking.Spool>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class UnloadSpoolRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string Action { get; set; } = string.Empty;

    /// <summary>Why the spool was finished; kept with the write-off. Ignored when returning to storage.</summary>
    public string? Reason { get; set; }

    public string EntityType => "Spool";

    public string EventType => Action == "MarkFinished" ? "SpoolFinished" : "SpoolReturnedToStorage";
}

public sealed class UnloadSpoolRequestValidator : AbstractValidator<UnloadSpoolRequest>
{
    public UnloadSpoolRequestValidator()
    {
        RuleFor(x => x.Action).Must(a => a is "ReturnToStorage" or "MarkFinished")
            .WithMessage("Action must be 'ReturnToStorage' or 'MarkFinished'.");
        RuleFor(x => x.Reason).MaximumLength(SpoolUsageRecord.MaxReasonLength);
    }
}

/// <summary>FR-007. Finishing works from a slot or storage; returning to storage only from a slot (422 otherwise). 409 when a concurrent conflicting change lost the race.</summary>
[Permission("spools:unload")]
public sealed class UnloadSpoolEndpoint(UnloadSpoolHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<UnloadSpoolRequest, SpoolResponse>
{
    public override void Configure()
    {
        Put("/api/v1/spools/{id}/unload");
        Policies("Permission");
    }

    public override async Task HandleAsync(UnloadSpoolRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var action = req.Action == "MarkFinished" ? UnloadSpoolAction.MarkFinished : UnloadSpoolAction.ReturnToStorage;
        var spool = await handler.HandleAsync(new UnloadSpoolCommand(req.Id, action, req.Reason), ct);

        Response = await SpoolResponseLookup.ResolveAsync(spoolsQuery, spool.Id, ct);
    }
}
