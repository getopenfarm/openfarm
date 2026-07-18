using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using CorrectRemainingWeightHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CorrectRemainingWeightCommand, OpenFarm.Domain.FilamentTracking.Spool>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class CorrectRemainingWeightRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public decimal RemainingWeightGrams { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolRemainingWeightCorrected";
}

public sealed class CorrectRemainingWeightRequestValidator : AbstractValidator<CorrectRemainingWeightRequest>
{
    public CorrectRemainingWeightRequestValidator()
    {
        RuleFor(x => x.RemainingWeightGrams).GreaterThanOrEqualTo(0);
    }
}

/// <summary>FR-012 — manual override at any time. 422 on a negative value or one exceeding SpoolWeightGrams.</summary>
[Permission("spools:override-weight")]
public sealed class CorrectRemainingWeightEndpoint(CorrectRemainingWeightHandler handler)
    : Endpoint<CorrectRemainingWeightRequest, SpoolResponseLite>
{
    public override void Configure()
    {
        Put("/api/v1/spools/{id}/remaining-weight");
        Policies("Permission");
    }

    public override async Task HandleAsync(CorrectRemainingWeightRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var command = new CorrectRemainingWeightCommand(req.Id, req.RemainingWeightGrams);
        var spool = await handler.HandleAsync(command, ct);

        Response = new SpoolResponseLite
        {
            Id = spool.Id,
            State = spool.State.ToString(),
            RemainingWeightGrams = spool.RemainingWeightGrams,
            HasUsageOverrun = spool.HasUsageOverrun,
        };
    }
}

public sealed class SpoolResponseLite
{
    public Guid Id { get; set; }

    public string State { get; set; } = string.Empty;

    public decimal RemainingWeightGrams { get; set; }

    public bool HasUsageOverrun { get; set; }
}
