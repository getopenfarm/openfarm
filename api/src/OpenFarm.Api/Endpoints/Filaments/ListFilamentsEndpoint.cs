using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class ListFilamentsResponse
{
    public IReadOnlyList<FilamentResponse> Filaments { get; set; } = [];
}

[Permission("filaments:list")]
public sealed class ListFilamentsEndpoint(IQueryHandler<GetFilamentsQuery, IReadOnlyList<FilamentSummary>> handler)
    : EndpointWithoutRequest<ListFilamentsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/filaments");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var filaments = await handler.HandleAsync(new GetFilamentsQuery(), ct);

        Response = new ListFilamentsResponse
        {
            Filaments = filaments.Select(FilamentResponse.FromSummary).ToList(),
        };
    }
}
