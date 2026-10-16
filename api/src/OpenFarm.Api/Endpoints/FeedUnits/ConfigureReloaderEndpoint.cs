using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Api.Processors;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.FeedUnits;

public sealed class ConfigureReloaderRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public Guid? ReplacesFeedUnitId { get; set; }
    public Guid? FeedingSlotId { get; set; }
    public bool TransferExternalSpool { get; set; } = true;
    public string EntityType => "FeedUnit";
    public string EventType => "FeedUnitConfigured";
}

[Permission("feed-units:configure")]
public sealed class ConfigureReloaderEndpoint(
    OpenFarm.Application.Common.ICommandHandler<ConfigureReloaderCommand, FeedUnit> handler)
    : Endpoint<ConfigureReloaderRequest>
{
    public override void Configure()
    {
        Put("/api/v1/feed-units/{id}/configuration");
        Policies("Permission");
    }
    public override async Task HandleAsync(ConfigureReloaderRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new(req.Id, req.ReplacesFeedUnitId, req.FeedingSlotId,
            req.TransferExternalSpool), ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}

public sealed class SelectReloaderSlotRequest : IActivityLoggable
{
    public Guid Id { get; set; }
    public Guid? FeedingSlotId { get; set; }
    public string EntityType => "FeedUnit";
    public string EventType => "FeedUnitFeedingChannelSelected";
}

[Permission("feed-units:configure")]
public sealed class SelectReloaderSlotEndpoint(
    OpenFarm.Application.Common.ICommandHandler<SelectReloaderSlotCommand, FeedUnit> handler)
    : Endpoint<SelectReloaderSlotRequest>
{
    public override void Configure()
    {
        Put("/api/v1/feed-units/{id}/feeding-slot");
        Policies("Permission");
    }
    public override async Task HandleAsync(SelectReloaderSlotRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await handler.HandleAsync(new(req.Id, req.FeedingSlotId), ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
