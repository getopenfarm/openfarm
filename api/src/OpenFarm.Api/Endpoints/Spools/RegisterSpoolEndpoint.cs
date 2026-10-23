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

/// <summary>One line of a delivery: identical spools of one colour.</summary>
public sealed class RegisterSpoolLineRequest
{
    public Guid FilamentVariantId { get; set; }

    public decimal SpoolWeightGrams { get; set; }

    /// <summary>How many identical spools, 1–500.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Grams left on each spool; omit for full.</summary>
    public decimal? RemainingWeightGrams { get; set; }

    /// <summary>What was paid for each whole spool; omit when unknown. Zero is a real price.</summary>
    public decimal? Price { get; set; }

    public string? Label { get; set; }
}

/// <summary>A delivery: up to 20 lines and 1,000 spools, sharing the supplier, order number and received date.</summary>
public sealed class RegisterSpoolRequest : IActivityLoggable
{
    public IReadOnlyList<RegisterSpoolLineRequest> Lines { get; set; } = [];

    public string? Supplier { get; set; }

    public string? OrderNumber { get; set; }

    public DateOnly? ReceivedOn { get; set; }

    /// <summary>The spool's RFID tag as a printer reads it; a printer that reads it loads the spool. Only for one line of one spool.</summary>
    public string? RfidId { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolRegistered";
}

public sealed class RegisterSpoolRequestValidator : AbstractValidator<RegisterSpoolRequest>
{
    public RegisterSpoolRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(RegisterSpoolCommand.MaxLines).OverridePropertyName("lines");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.FilamentVariantId).NotEmpty();
            line.RuleFor(l => l.SpoolWeightGrams).GreaterThan(0);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, RegisterSpoolCommand.MaxQuantity);
            line.RuleFor(l => l.RemainingWeightGrams).GreaterThan(0).When(l => l.RemainingWeightGrams is not null);
            line.RuleFor(l => l.RemainingWeightGrams)
                .Must((l, remaining) => remaining is null || remaining <= l.SpoolWeightGrams)
                .WithMessage("Remaining weight cannot exceed spool weight.");
            line.RuleFor(l => l.Price).GreaterThanOrEqualTo(0).When(l => l.Price is not null);
        });
        // Summed as long: a huge quantity on a line that already failed its own rule must not overflow here.
        RuleFor(x => x.Lines)
            .Must(lines => lines.Sum(l => (long)l.Quantity) <= RegisterSpoolCommand.MaxSpools)
            .WithMessage($"A delivery adds at most {RegisterSpoolCommand.MaxSpools} spools.");
        RuleFor(x => x.Supplier).MaximumLength(Spool.MaxPurchaseTextLength);
        RuleFor(x => x.OrderNumber).MaximumLength(Spool.MaxPurchaseTextLength);
        RuleFor(x => x.RfidId).MaximumLength(Spool.MaxRfidIdLength);
        RuleFor(x => x.Lines)
            .Must(lines => lines.Count == 1 && lines[0].Quantity == 1)
            .When(x => x.RfidId is not null)
            .WithMessage("A tagged spool is added one at a time.");
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

/// <summary>FR-001/FR-002. Adds a delivery's spools in one save. 404 on an unresolvable filamentVariantId; 400 or 422 on validation failures; 409 when the RFID tag is already registered.</summary>
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
            req.Lines.Select(l => new RegisterSpoolLine(l.FilamentVariantId, l.SpoolWeightGrams, l.Quantity, l.RemainingWeightGrams, l.Price, l.Label)).ToList(),
            req.Supplier, req.OrderNumber, req.ReceivedOn, req.RfidId);
        var spools = await handler.HandleAsync(command, ct);

        Response = new RegisterSpoolsResponse
        {
            Spools = await SpoolResponseLookup.ResolveManyAsync(spoolsQuery, spools.Select(s => s.Id).ToList(), ct),
        };
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
