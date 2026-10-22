using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class FilamentShortageResponse
{
    public Guid FilamentVariantId { get; set; }

    /// <summary>Grams queued, printing and paused finite jobs take of this colour.</summary>
    public decimal QueuedGrams { get; set; }

    public decimal OnHandGrams { get; set; }
}

public sealed class ListFilamentShortagesResponse
{
    public IReadOnlyList<FilamentShortageResponse> Shortages { get; set; } = [];
}

/// <summary>GET /api/v1/filaments/shortages — colours queued finite work needs more of than is on hand. Apart from the filament list so pickers don't pay for the job scan.</summary>
[Permission("filaments:list")]
public sealed class ListFilamentShortagesEndpoint(IQueryHandler<GetFilamentShortagesQuery, IReadOnlyList<FilamentShortage>> handler)
    : EndpointWithoutRequest<ListFilamentShortagesResponse>
{
    public override void Configure()
    {
        Get("/api/v1/filaments/shortages");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shortages = await handler.HandleAsync(new GetFilamentShortagesQuery(), ct);
        Response = new ListFilamentShortagesResponse
        {
            Shortages = shortages.Select(s => new FilamentShortageResponse
            {
                FilamentVariantId = s.FilamentVariantId,
                QueuedGrams = s.QueuedGrams,
                OnHandGrams = s.OnHandGrams,
            }).ToList(),
        };
    }
}
