using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using RecordSpoolUsageHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RecordSpoolUsageCommand, OpenFarm.Application.FilamentTracking.RecordSpoolUsageResult>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class RecordSpoolUsageRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public decimal GramsConsumed { get; set; }

    public Guid? JobId { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolUsageRecorded";
}

public sealed class RecordSpoolUsageRequestValidator : AbstractValidator<RecordSpoolUsageRequest>
{
    public RecordSpoolUsageRequestValidator()
    {
        RuleFor(x => x.GramsConsumed).GreaterThan(0);
    }
}

public sealed class AutoContinuationResponse
{
    public bool Occurred { get; set; }

    public Guid? ContinuedSpoolId { get; set; }

    public Guid? ContinuedFeedSlotId { get; set; }

    public bool MaterialMismatch { get; set; }

    public static AutoContinuationResponse FromResult(AutoContinuationResult result) => new()
    {
        Occurred = result.Occurred,
        ContinuedSpoolId = result.ContinuedSpoolId,
        ContinuedFeedSlotId = result.ContinuedFeedSlotId,
        MaterialMismatch = result.MaterialMismatch,
    };
}

public sealed class RecordSpoolUsageResponse
{
    public Guid Id { get; set; }

    public string State { get; set; } = string.Empty;

    public decimal RemainingWeightGrams { get; set; }

    public bool HasUsageOverrun { get; set; }

    public AutoContinuationResponse AutoContinuation { get; set; } = new();
}

/// <summary>FR-006, FR-009 — 409 when the spool isn't currently Using.</summary>
[Permission("spools:record-usage")]
public sealed class RecordSpoolUsageEndpoint(RecordSpoolUsageHandler handler) : Endpoint<RecordSpoolUsageRequest, RecordSpoolUsageResponse>
{
    public override void Configure()
    {
        Post("/api/v1/spools/{id}/usage");
        Policies("Permission");
    }

    public override async Task HandleAsync(RecordSpoolUsageRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var result = await handler.HandleAsync(new RecordSpoolUsageCommand(req.Id, req.GramsConsumed, req.JobId), ct);

        Response = new RecordSpoolUsageResponse
        {
            Id = result.Spool.Id,
            State = result.Spool.State.ToString(),
            RemainingWeightGrams = result.Spool.RemainingWeightGrams,
            HasUsageOverrun = result.Spool.HasUsageOverrun,
            AutoContinuation = AutoContinuationResponse.FromResult(result.AutoContinuation),
        };
    }
}
