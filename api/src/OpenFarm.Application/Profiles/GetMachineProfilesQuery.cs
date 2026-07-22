using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// All fields null/omitted reproduces feature 003's exact original call (FR-013) — every other
// combination is the US2 picker's paginated/searchable path (research.md §6).
public sealed record GetMachineProfilesQuery(
    string? Origin = null,
    string? SearchText = null,
    int? Page = null,
    int? PageSize = null);

public sealed record MachineProfileSummary(
    Guid Id,
    string Name,
    ProfileOrigin Origin,
    string? VendorName,
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

public sealed class GetMachineProfilesQueryHandler(IMachineProfileRepository repository, IProfileBundleParser parser)
    : IQueryHandler<GetMachineProfilesQuery, ProfilePage<MachineProfileSummary>>
{
    private const int MaxChainDepth = 32;

    public async Task<ProfilePage<MachineProfileSummary>> HandleAsync(GetMachineProfilesQuery query, CancellationToken cancellationToken)
    {
        var isPickerStyleCall = query.Origin is not null || query.SearchText is not null || query.Page is not null || query.PageSize is not null;

        if (!isPickerStyleCall)
        {
            return await BuildLegacyBrowsePageResultAsync(cancellationToken);
        }

        // US2 picker path (research.md §6) — the DB/repository already narrowed and paginated the
        // rows, so lean fields are returned directly with no per-row ancestor-chain merge, keeping
        // this responsive at a vendor's full ingested scale (SC-010) regardless of total catalog size.
        var page = query.Page ?? 1;
        var (rows, totalCount) = await repository.SearchAsync(
            ProfileOriginQueryParser.ParseEffectiveFilter(query.Origin), true, null,
            null, query.SearchText, page, query.PageSize ?? 100_000, cancellationToken);

        var leanSummaries = rows.Select(r => new MachineProfileSummary(
            r.Id, r.Name, r.Origin, r.VendorName, r.SourceFormat, r.BaseProfileId, r.BaseProfileName,
            false, null, null, null, null, null, [])).ToList();

        // "Omit page" means one page containing everything (contracts/profiles-api.md) — the
        // reported pageSize then reflects the actual row count, not the internal unbounded cap.
        var reportedPageSize = query.PageSize ?? totalCount;

        return new ProfilePage<MachineProfileSummary>(leanSummaries, page, reportedPageSize, totalCount);
    }

    // Feature 003, FR-010/US3's original browse page, restricted to User rows only now that System
    // rows exist in the same table (009 FR-013 — System profiles are picker-only, never browsable/
    // importable here). `byId` still spans every origin so a User row's BaseProfileId chain can
    // resolve through a System ancestor; only the emitted top-level rows are User-filtered.
    private async Task<ProfilePage<MachineProfileSummary>> BuildLegacyBrowsePageResultAsync(CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Id);
        var userRows = rows.Where(r => r.Origin == ProfileOrigin.User).ToList();

        var summaries = new List<MachineProfileSummary>(userRows.Count);

        foreach (var row in userRows)
        {
            var (ancestorsOldestFirst, hasUnresolvedBase) = WalkInMemory(row.Id, byId);
            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            var fields = parser.ExtractMachineDisplayFields(merged, row.SourceFormat);

            summaries.Add(new MachineProfileSummary(
                row.Id, row.Name, row.Origin, row.VendorName, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.NozzleSizeMm, fields.BedSizeXMm, fields.BedSizeYMm, fields.MaxHeightMm, fields.HasEnclosure, fields.SupportedMaterials));
        }

        return new ProfilePage<MachineProfileSummary>(summaries, 1, summaries.Count, summaries.Count);
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
