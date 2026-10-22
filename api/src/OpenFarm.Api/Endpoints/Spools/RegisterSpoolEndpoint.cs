using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using RegisterSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RegisterSpoolCommand, System.Collections.Generic.IReadOnlyList<OpenFarm.Domain.FilamentTracking.Spool>>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class RegisterSpoolRequest : IActivityLoggable
{
    public Guid FilamentVariantId { get; set; }

    public string? Label { get; set; }

    public decimal SpoolWeightGrams { get; set; }

    public decimal? RemainingWeightGrams { get; set; }

    /// <summary>How many identical spools to add, 1–50. The label, weights and purchase info apply to each.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>What was paid for each whole spool; omit when unknown. Zero is a real price.</summary>
    public decimal? Price { get; set; }

    public string? Supplier { get; set; }

    public string? OrderNumber { get; set; }

    public DateOnly? ReceivedOn { get; set; }

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
        RuleFor(x => x.Quantity).InclusiveBetween(1, RegisterSpoolCommand.MaxQuantity);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).When(x => x.Price is not null);
        RuleFor(x => x.Supplier).MaximumLength(Spool.MaxPurchaseTextLength);
        RuleFor(x => x.OrderNumber).MaximumLength(Spool.MaxPurchaseTextLength);
    }
}

public sealed class RegisterSpoolsResponse : IHasEntityIds
{
    public IReadOnlyList<SpoolResponse> Spools { get; set; } = [];

    // Internal to the post-processor extension only: one registration can add many spools and each is logged
    // (mirrors ConfirmProfileImportResponse's precedent), so it is excluded from JSON serialization.
    [JsonIgnore]
    public IReadOnlyList<string> EntityIds => Spools.Select(s => s.Id.ToString()).ToList();
}

/// <summary>FR-001/FR-002. Adds <c>quantity</c> identical spools in one save. 404 on an unresolvable filamentVariantId; 400 or 422 on validation failures.</summary>
[Permission("spools:register")]
public sealed class RegisterSpoolEndpoint(RegisterSpoolHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<RegisterSpoolRequest, RegisterSpoolsResponse>
{
    public override void Configure()
    {
        Post("/api/v1/spools");
        Policies("Permission");
    }

    public override async Task HandleAsync(RegisterSpoolRequest req, CancellationToken ct)
    {
        var command = new RegisterSpoolCommand(
            req.FilamentVariantId, req.Label, req.SpoolWeightGrams, req.RemainingWeightGrams,
            req.Quantity, req.Price, req.Supplier, req.OrderNumber, req.ReceivedOn);
        var spools = await handler.HandleAsync(command, ct);

        Response = new RegisterSpoolsResponse
        {
            Spools = await SpoolResponseLookup.ResolveManyAsync(spoolsQuery, spools.Select(s => s.Id).ToList(), ct),
        };
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
