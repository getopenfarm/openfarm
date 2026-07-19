using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using RegisterSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RegisterSpoolCommand, OpenFarm.Domain.FilamentTracking.Spool>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class RegisterSpoolRequest : IActivityLoggable
{
    public Guid FilamentVariantId { get; set; }

    public string? Label { get; set; }

    public decimal SpoolWeightGrams { get; set; }

    public decimal? RemainingWeightGrams { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolRegistered";
}

public sealed class RegisterSpoolRequestValidator : AbstractValidator<RegisterSpoolRequest>
{
    public RegisterSpoolRequestValidator()
    {
        RuleFor(x => x.FilamentVariantId).NotEmpty();
        RuleFor(x => x.SpoolWeightGrams).GreaterThan(0);
        RuleFor(x => x.RemainingWeightGrams).GreaterThan(0).When(x => x.RemainingWeightGrams is not null);
        RuleFor(x => x.RemainingWeightGrams)
            .Must((request, remaining) => remaining is null || remaining <= request.SpoolWeightGrams)
            .WithMessage("Remaining weight cannot exceed spool weight.")
            .When(x => x.RemainingWeightGrams is not null);
    }
}

/// <summary>FR-001/FR-002. 404 on an unresolvable filamentVariantId; 422 on weight validation failures.</summary>
[Permission("spools:register")]
public sealed class RegisterSpoolEndpoint(RegisterSpoolHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<RegisterSpoolRequest, SpoolResponse>
{
    public override void Configure()
    {
        Post("/api/v1/spools");
        Policies("Permission");
    }

    public override async Task HandleAsync(RegisterSpoolRequest req, CancellationToken ct)
    {
        var command = new RegisterSpoolCommand(req.FilamentVariantId, req.Label, req.SpoolWeightGrams, req.RemainingWeightGrams);
        var spool = await handler.HandleAsync(command, ct);

        Response = await SpoolResponseLookup.ResolveAsync(spoolsQuery, spool.Id, ct);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
