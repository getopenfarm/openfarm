using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.FeedUnits;

public sealed class ListFeedUnitsRequest
{
    public Guid PrinterId { get; set; }
    public bool? IncludeHidden { get; set; }
    public bool? IncludeArchived { get; set; }
}

public sealed class ListFeedUnitsResponse
{
    public IReadOnlyList<FeedUnitResponse> FeedUnits { get; set; } = [];
}

/// <summary>404 on an unresolved printerId; omits a superseded, unrevealed, unloaded External Spool by default (research.md §12).</summary>
[Permission("feed-units:list")]
public sealed class ListFeedUnitsEndpoint(IQueryHandler<GetFeedUnitsQuery, IReadOnlyList<FeedUnitDetail>> handler)
    : Endpoint<ListFeedUnitsRequest, ListFeedUnitsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/printers/{printerId}/feed-units");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListFeedUnitsRequest req, CancellationToken ct)
    {
        req.PrinterId = Route<Guid>("printerId");

        var feedUnits = await handler.HandleAsync(new GetFeedUnitsQuery(req.PrinterId, req.IncludeHidden == true, req.IncludeArchived == true), ct);

        Response = new ListFeedUnitsResponse
        {
            FeedUnits = feedUnits.Select(FeedUnitResponse.FromDetail).ToList(),
        };
    }
}
