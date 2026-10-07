using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using AddFeedUnitHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.AddFeedUnitCommand, OpenFarm.Domain.FilamentTracking.FeedUnit>;

namespace OpenFarm.Api.Endpoints.FeedUnits;

public sealed class AddFeedUnitRequest : IActivityLoggable
{
    public Guid PrinterId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? Name { get; set; }

    public int? SlotCount { get; set; }

    public Guid? ReplacesFeedUnitId { get; set; }
    public int? StartingSlotPosition { get; set; }
    public bool TransferExternalSpool { get; set; } = true;

    public string EntityType => "FeedUnit";

    public string EventType => Kind == FeedUnitKindCatalog.ExternalSpool ? "ExternalSpoolRevealed" : "FeedUnitAdded";
}

public sealed class AddFeedUnitRequestValidator : AbstractValidator<AddFeedUnitRequest>
{
    public AddFeedUnitRequestValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is FeedUnitKindCatalog.AutomaticFilamentReloader or FeedUnitKindCatalog.ExternalSpool);
        RuleFor(x => x.StartingSlotPosition).Must((x, position) => position is null
            || position >= 0 && position < x.SlotCount)
            .WithMessage("Starting channel must be within the declared slot count.");
        RuleFor(x => x.Name).NotEmpty().When(x => x.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader);
        RuleFor(x => x.SlotCount).NotNull().GreaterThanOrEqualTo(1).LessThanOrEqualTo(100).When(x => x.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader);
    }
}

public sealed class AddFeedUnitResponse : IHasEntityIds
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public int UnitIndex { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsRevealed { get; set; }

    public Guid? ReplacesFeedUnitId { get; set; }
    public Guid? FeedingSlotId { get; set; }
    public string? FeedingSelectionSource { get; set; }

    public IReadOnlyList<FeedSlotResponse> FeedSlots { get; set; } = [];

    // Internal to the post-processor extension only — not part of the public response contract, so
    // excluded from JSON serialization (mirrors ConfirmProfileImportResponse's precedent).
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EntityIds => new[] { Id.ToString() }.Concat(FeedSlots.Select(s => s.Id.ToString())).ToList();
}

/// <summary>
/// "Add a Feed Unit" — the unified action covering both declaring an Automatic Filament Reloader and
/// revealing an already-detected-but-hidden External Spool (research.md §13). 409 on a duplicate AFR
/// name; 422 on an invalid slotCount/replacesFeedUnitId; 404 if the printer never reported an
/// External Spool at all (FR-026).
/// </summary>
[Permission("feed-units:add")]
public sealed class AddFeedUnitEndpoint(AddFeedUnitHandler handler, IQueryHandler<GetFeedUnitsQuery, IReadOnlyList<FeedUnitDetail>> feedUnitsQuery)
    : Endpoint<AddFeedUnitRequest, AddFeedUnitResponse>
{
    public override void Configure()
    {
        Post("/api/v1/printers/{printerId}/feed-units");
        Policies("Permission");
    }

    public override async Task HandleAsync(AddFeedUnitRequest req, CancellationToken ct)
    {
        req.PrinterId = Route<Guid>("printerId");

        var command = new AddFeedUnitCommand(req.PrinterId, req.Kind, req.Name, req.SlotCount, req.ReplacesFeedUnitId, req.StartingSlotPosition, req.TransferExternalSpool);
        var unit = await handler.HandleAsync(command, ct);

        var feedUnits = await feedUnitsQuery.HandleAsync(new GetFeedUnitsQuery(req.PrinterId), ct);
        var detail = feedUnits.First(u => u.Id == unit.Id);

        Response = new AddFeedUnitResponse
        {
            Id = detail.Id,
            Kind = detail.Kind,
            UnitIndex = detail.UnitIndex,
            DisplayLabel = detail.DisplayLabel,
            IsRevealed = detail.IsRevealed,
            ReplacesFeedUnitId = detail.ReplacesFeedUnitId,
            FeedingSlotId = detail.FeedingSlotId,
            FeedingSelectionSource = detail.FeedingSelectionSource,
            FeedSlots = detail.FeedSlots.Select(FeedSlotResponse.FromDetail).ToList(),
        };

        HttpContext.Response.StatusCode = req.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;
    }
}
