using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public sealed record GetProcessProfilesQuery;

public sealed record ProcessProfileSummary(
    Guid Id,
    string Name,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    double? LayerHeightMm,
    int? WallLoops,
    IReadOnlyList<string> CompatiblePrinterNames);

public sealed class GetProcessProfilesQueryHandler(IProcessProfileRepository repository, IProfileBundleParser parser)
    : IQueryHandler<GetProcessProfilesQuery, IReadOnlyList<ProcessProfileSummary>>
{
    private const int MaxChainDepth = 32;

    public async Task<IReadOnlyList<ProcessProfileSummary>> HandleAsync(GetProcessProfilesQuery query, CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Id);

        var summaries = new List<ProcessProfileSummary>(rows.Count);

        foreach (var row in rows)
        {
            var (ancestorsOldestFirst, hasUnresolvedBase) = WalkInMemory(row.Id, byId);
            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            var fields = parser.ExtractProcessDisplayFields(merged, row.SourceFormat);

            summaries.Add(new ProcessProfileSummary(
                row.Id, row.Name, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.LayerHeightMm, fields.WallLoops, fields.CompatiblePrinterNames));
        }

        return summaries;
    }

    private static (List<string> AncestorsOldestFirst, bool HasUnresolvedBase) WalkInMemory(
        Guid startId, Dictionary<Guid, ProcessProfile> byId)
    {
        var chainNewestFirst = new List<string>();
        var visited = new HashSet<Guid>();
        var hasUnresolvedBase = false;
        Guid? currentId = startId;
        var depth = 0;

        while (currentId is not null)
        {
            if (depth++ >= MaxChainDepth || !visited.Add(currentId.Value) || !byId.TryGetValue(currentId.Value, out var row))
            {
                hasUnresolvedBase = true;
                break;
            }

            chainNewestFirst.Add(row.RawDataJson);
            if (row.BaseProfileId is null && row.BaseProfileName is not null)
            {
                hasUnresolvedBase = true;
            }

            currentId = row.BaseProfileId;
        }

        chainNewestFirst.Reverse();
        return (chainNewestFirst, hasUnresolvedBase);
    }
}
