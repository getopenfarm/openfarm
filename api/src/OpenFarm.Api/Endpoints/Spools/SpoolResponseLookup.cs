using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

/// <summary>Resolves a single spool's fully-joined <see cref="SpoolResponse"/> (after a mutation, or for the spool page), reusing <see cref="GetSpoolsQueryHandler"/>'s join rather than duplicating it per endpoint. An unknown id is a 404.</summary>
internal static class SpoolResponseLookup
{
    public static async Task<SpoolResponse> ResolveAsync(IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery, Guid spoolId, CancellationToken cancellationToken)
    {
        var spools = await spoolsQuery.HandleAsync(new GetSpoolsQuery(null, null, null, null, SpoolId: spoolId), cancellationToken);
        return spools.Count == 0 ? throw new SpoolNotFoundException(spoolId) : SpoolResponse.FromSummary(spools[0]);
    }

    /// <summary>The same read for several spools in one query, such as the ones a single registration added.</summary>
    public static async Task<IReadOnlyList<SpoolResponse>> ResolveManyAsync(IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery, IReadOnlyCollection<Guid> spoolIds, CancellationToken cancellationToken)
    {
        var spools = await spoolsQuery.HandleAsync(new GetSpoolsQuery(null, null, null, null, SpoolIds: spoolIds), cancellationToken);
        return spools.Select(SpoolResponse.FromSummary).ToList();
    }
}
