using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

/// <summary>Resolves a single spool's fully-joined <see cref="SpoolResponse"/> after a mutation, reusing <see cref="GetSpoolsQueryHandler"/>'s join rather than duplicating it per endpoint.</summary>
internal static class SpoolResponseLookup
{
    public static async Task<SpoolResponse> ResolveAsync(IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>> spoolsQuery, Guid spoolId, CancellationToken cancellationToken)
    {
        var spools = await spoolsQuery.HandleAsync(new GetSpoolsQuery(null, null, null, null), cancellationToken);
        var summary = spools.First(s => s.Id == spoolId);
        return SpoolResponse.FromSummary(summary);
    }
}
