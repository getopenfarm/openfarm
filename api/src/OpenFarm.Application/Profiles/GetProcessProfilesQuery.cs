using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// All fields null/omitted reproduces feature 003's exact original call (FR-013) — every other
// combination is the US2 picker's paginated/searchable path (research.md §6). `PrinterId` is sugar
// over `CompatibleWithMachineName` (contracts/profiles-api.md) — resolved server-side so the
// frontend never looks up a machine profile's name itself.
public sealed record GetProcessProfilesQuery(
    string? Origin = null,
    string? SearchText = null,
    string? CompatibleWithMachineName = null,
    Guid? PrinterId = null,
    int? Page = null,
    int? PageSize = null);

public sealed record ProcessProfileSummary(
    Guid Id,
    string Name,
    ProfileOrigin Origin,
    string? VendorName,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    double? LayerHeightMm,
    int? WallLoops,
    IReadOnlyList<string> CompatiblePrinterNames);

public sealed class GetProcessProfilesQueryHandler(
    IProcessProfileRepository repository,
    IProfileBundleParser parser,
    IPrinterRepository printerRepository,
    IMachineProfileRepository machineProfileRepository)
    : IQueryHandler<GetProcessProfilesQuery, ProfilePage<ProcessProfileSummary>>
{
    private const int MaxChainDepth = 32;

    public async Task<ProfilePage<ProcessProfileSummary>> HandleAsync(GetProcessProfilesQuery query, CancellationToken cancellationToken)
    {
        var isPickerStyleCall = query.Origin is not null || query.SearchText is not null
            || query.CompatibleWithMachineName is not null || query.PrinterId is not null
            || query.Page is not null || query.PageSize is not null;

        if (!isPickerStyleCall)
        {
            return await BuildLegacyBrowsePageResultAsync(cancellationToken);
        }

        var compatibleWithMachineName = query.CompatibleWithMachineName;
        if (query.PrinterId is { } printerId)
        {
            var printer = await printerRepository.GetByIdAsync(printerId, cancellationToken);
            if (printer?.MachineProfileId is not { } machineProfileId)
            {
                // FR-007 — nothing to select yet until a machine profile is linked.
                return new ProfilePage<ProcessProfileSummary>([], 1, 0, 0, Available: false);
            }

            var machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken);
            compatibleWithMachineName = machineProfile?.Name;
        }

        var page = query.Page ?? 1;
        var (rows, totalCount) = await repository.SearchAsync(
            ProfileOriginQueryParser.ParseEffectiveFilter(query.Origin), true, compatibleWithMachineName,
            null, query.SearchText, page, query.PageSize ?? 100_000, cancellationToken);

        var leanSummaries = rows.Select(r => new ProcessProfileSummary(
            r.Id, r.Name, r.Origin, r.VendorName, r.SourceFormat, r.BaseProfileId, r.BaseProfileName,
            false, null, null, [])).ToList();

        var reportedPageSize = query.PageSize ?? totalCount;
        var result = new ProfilePage<ProcessProfileSummary>(leanSummaries, page, reportedPageSize, totalCount);
        return query.PrinterId is not null ? result with { Available = true } : result;
    }

    private async Task<ProfilePage<ProcessProfileSummary>> BuildLegacyBrowsePageResultAsync(CancellationToken cancellationToken)
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
                row.Id, row.Name, row.Origin, row.VendorName, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.LayerHeightMm, fields.WallLoops, fields.CompatiblePrinterNames));
        }

        return new ProfilePage<ProcessProfileSummary>(summaries, 1, summaries.Count, summaries.Count);
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
