using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public sealed record GetFilamentProfilesQuery;

public sealed record FilamentProfileSummary(
    Guid Id,
    string Name,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    string? Material,
    string? Brand,
    string? ColorHex,
    double? NozzleSizeMm,
    double? NozzleTempC,
    double? BedTempC,
    double? MaxVolumetricSpeedMm3s,
    int? FanPercent,
    IReadOnlyList<string> CompatiblePrinterNames);

public sealed class GetFilamentProfilesQueryHandler(IFilamentProfileRepository repository, IProfileBundleParser parser)
    : IQueryHandler<GetFilamentProfilesQuery, IReadOnlyList<FilamentProfileSummary>>
{
    private const int MaxChainDepth = 32;

    public async Task<IReadOnlyList<FilamentProfileSummary>> HandleAsync(GetFilamentProfilesQuery query, CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Id);

        var summaries = new List<FilamentProfileSummary>(rows.Count);

        foreach (var row in rows)
        {
            var (ancestorsOldestFirst, hasUnresolvedBase) = WalkInMemory(row.Id, byId);
            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            var fields = parser.ExtractFilamentDisplayFields(merged, row.SourceFormat, row.Name, hasBaseProfileName: row.BaseProfileName is not null);

            summaries.Add(new FilamentProfileSummary(
                row.Id, row.Name, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.Material, fields.Brand, fields.ColorHex, fields.NozzleSizeMm, fields.NozzleTempC, fields.BedTempC,
                fields.MaxVolumetricSpeedMm3s, fields.FanPercent, fields.CompatiblePrinterNames));
        }

        return summaries;
    }

    private static (List<string> AncestorsOldestFirst, bool HasUnresolvedBase) WalkInMemory(
        Guid startId, Dictionary<Guid, FilamentProfile> byId)
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
