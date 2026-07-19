using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using LoadSpoolHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.LoadSpoolOntoFeedSlotCommand, OpenFarm.Application.FilamentTracking.LoadSpoolResult>;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class LoadSpoolRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public Guid FeedSlotId { get; set; }

    public string EntityType => "Spool";

    public string EventType => "SpoolLoaded";
}

public sealed class LoadSpoolRequestValidator : AbstractValidator<LoadSpoolRequest>
{
    public LoadSpoolRequestValidator()
    {
        RuleFor(x => x.FeedSlotId).NotEmpty();
    }
}

public sealed class LoadSpoolResponse
{
    public Guid Id { get; set; }

    public string? Label { get; set; }

    public Guid FilamentVariantId { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    public string VendorName { get; set; } = string.Empty;

    public decimal SpoolWeightGrams { get; set; }

    public decimal RemainingWeightGrams { get; set; }

    public string State { get; set; } = string.Empty;

    public Guid? FeedSlotId { get; set; }

    public string? FeedSlotLabel { get; set; }

    public bool HasUsageOverrun { get; set; }

    public bool RfidMismatchWarning { get; set; }

    public string? PortSharingNote { get; set; }
}

/// <summary>FR-005/FR-021/FR-022/FR-023. 404 spool/feed slot; 409 not InStorage, deactivated slot, already-occupied, or a concurrency conflict.</summary>
[Permission("spools:load")]
public sealed class LoadSpoolEndpoint(LoadSpoolHandler handler, IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery)
    : Endpoint<LoadSpoolRequest, LoadSpoolResponse>
{
    public override void Configure()
    {
        Put("/api/v1/spools/{id}/load");
        Policies("Permission");
    }

    public override async Task HandleAsync(LoadSpoolRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        var result = await handler.HandleAsync(new LoadSpoolOntoFeedSlotCommand(req.Id, req.FeedSlotId), ct);
        var joined = await SpoolResponseLookup.ResolveAsync(spoolsQuery, result.Spool.Id, ct);

        Response = new LoadSpoolResponse
        {
            Id = joined.Id,
            Label = joined.Label,
            FilamentVariantId = joined.FilamentVariantId,
            ColorName = joined.ColorName,
            ColorHex = joined.ColorHex,
            Material = joined.Material,
            VendorName = joined.VendorName,
            SpoolWeightGrams = joined.SpoolWeightGrams,
            RemainingWeightGrams = joined.RemainingWeightGrams,
            State = joined.State,
            FeedSlotId = joined.FeedSlotId,
            FeedSlotLabel = joined.FeedSlotLabel,
            HasUsageOverrun = joined.HasUsageOverrun,
            RfidMismatchWarning = result.RfidMismatchWarning,
            PortSharingNote = result.PortSharingNote,
        };
    }
}
