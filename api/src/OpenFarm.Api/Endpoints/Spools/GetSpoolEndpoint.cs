using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

/// <summary>GET /api/v1/spools/{id} — one spool, fully joined, so a spool page need not read the whole inventory. 404 for an unknown id.</summary>
[Permission("spools:list")]
public sealed class GetSpoolEndpoint(IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery) : EndpointWithoutRequest<SpoolResponse>
{
    public override void Configure()
    {
        Get("/api/v1/spools/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        Response = await SpoolResponseLookup.ResolveAsync(spoolsQuery, Route<Guid>("id"), ct);
}
