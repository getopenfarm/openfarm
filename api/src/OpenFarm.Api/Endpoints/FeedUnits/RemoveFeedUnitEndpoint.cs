using FastEndpoints;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using RemoveFeedUnitHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RemoveFeedUnitCommand, OpenFarm.Domain.FilamentTracking.FeedUnit>;

namespace OpenFarm.Api.Endpoints.FeedUnits;

public sealed class RemoveFeedUnitRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string EntityType => "FeedUnit";

    public string EventType => "FeedUnitRemoved";
}

/// <summary>
/// "Remove a Feed Unit" — deactivates an Automatic Filament Reloader or re-hides an External Spool
/// position (research.md §13). 404 unresolved; 422 against a connector-derived Multi-Material Unit
/// (FR-005a).
/// </summary>
[Permission("feed-units:remove")]
public sealed class RemoveFeedUnitEndpoint(RemoveFeedUnitHandler handler) : Endpoint<RemoveFeedUnitRequest>
{
    public override void Configure()
    {
        Delete("/api/v1/feed-units/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(RemoveFeedUnitRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");

        await handler.HandleAsync(new RemoveFeedUnitCommand(req.Id), ct);

        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
