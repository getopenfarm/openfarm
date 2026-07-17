using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public sealed record GetMachineProfilesQuery;

public sealed record MachineProfileSummary(
    Guid Id,
    string Name,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    double? NozzleSizeMm,
    double? BedSizeXMm,
    double? BedSizeYMm,
    double? MaxHeightMm,
    bool? HasEnclosure,
    IReadOnlyList<string> SupportedMaterials);

// One ListAsync call, then walks each row's BaseProfileId chain purely in memory over that same
// fetched set (research.md §10) — no extra DB round trips, no recursive CTE (this feature's scale
// doesn't share BOM decomposition's unlimited-depth story, constitution Principle V's own carve-out).
public sealed class GetMachineProfilesQueryHandler(IMachineProfileRepository repository, IProfileBundleParser parser)
    : IQueryHandler<GetMachineProfilesQuery, IReadOnlyList<MachineProfileSummary>>
{
    private const int MaxChainDepth = 32;

    public async Task<IReadOnlyList<MachineProfileSummary>> HandleAsync(GetMachineProfilesQuery query, CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Id);

        var summaries = new List<MachineProfileSummary>(rows.Count);

        foreach (var row in rows)
        {
            var (ancestorsOldestFirst, hasUnresolvedBase) = WalkInMemory(row.Id, byId);
            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            var fields = parser.ExtractMachineDisplayFields(merged, row.SourceFormat);

            summaries.Add(new MachineProfileSummary(
                row.Id, row.Name, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.NozzleSizeMm, fields.BedSizeXMm, fields.BedSizeYMm, fields.MaxHeightMm, fields.HasEnclosure, fields.SupportedMaterials));
        }

        return summaries;
    }

    private static (List<string> AncestorsOldestFirst, bool HasUnresolvedBase) WalkInMemory(
        Guid startId, Dictionary<Guid, MachineProfile> byId)
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
