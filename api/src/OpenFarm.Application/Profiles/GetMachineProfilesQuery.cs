using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// All fields null/omitted reproduces feature 003's exact original call (FR-013) — every other
// combination is the US2 picker's paginated/searchable path (research.md §6).
public sealed record GetMachineProfilesQuery(
    string? Origin = null,
    string? SearchText = null,
    int? Page = null,
    int? PageSize = null, Guid? PrinterId = null, ListSort? Sorting = null);

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
    IReadOnlyList<string> SupportedMaterials)
{
    /// <summary>The slicer version this System row belongs to; <c>null</c> for User rows.</summary>
    public string? SourceVersion { get; init; }
}

public sealed class GetMachineProfilesQueryHandler(IMachineProfileRepository repository, IProfileBundleParser parser, IPrinterRepository printers, ProfileCompatibility compatibility,
    ProfileCatalogVersions? catalogVersions = null)
    : IQueryHandler<GetMachineProfilesQuery, ProfilePage<MachineProfileSummary>>
{
    public async Task<ProfilePage<MachineProfileSummary>> HandleAsync(GetMachineProfilesQuery query, CancellationToken cancellationToken)
    {
        var scope = catalogVersions is null ? null : await catalogVersions.GetScopeAsync(cancellationToken);
        if (query.PrinterId is { } printerId)
        {
            var printer = await printers.GetByIdAsync(printerId, cancellationToken) ?? throw new PrinterNotFoundException(printerId);
            var matches = new List<MachineProfileSummary>();
            var nozzles = printer.ConfirmedNozzlesMm();
            if (nozzles.Count > 0)
                foreach (var profile in await repository.ListAsync(cancellationToken, scope))
                {
                    if (query.Origin is { } origin && origin != "All" && profile.Origin.ToString() != origin
                        || !string.IsNullOrWhiteSpace(query.SearchText) && !profile.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        // A preset at any of the printer's confirmed nozzles fits it; jobs then need that nozzle.
                        foreach (var nozzle in nozzles)
                        {
                            if (!await compatibility.MachineMatchesAsync(profile, printer.Brand, printer.ModelCode, nozzle, cancellationToken)) continue;
                            matches.Add(new(profile.Id, profile.Name, profile.Origin, profile.VendorName, profile.SourceFormat,
                                profile.BaseProfileId, profile.BaseProfileName, false, (double)nozzle, null, null, null, null, [])
                            { SourceVersion = profile.SourceVersion });
                            break;
                        }
                    }
                    catch (SlicingProfileUnconfiguredException) { }
                }
            var size = Math.Clamp(query.PageSize ?? 50, 1, 1000);
            var requestedPage = Math.Max(query.Page ?? 1, 1);
            return new((query.Sorting ?? new ListSort("name", false)).Order(matches, p => p.Name, p => p.Id).Skip((requestedPage - 1) * size).Take(size).ToList(), requestedPage, size, matches.Count);
        }
        var isPickerStyleCall = query.Origin is not null || query.SearchText is not null || query.Page is not null || query.PageSize is not null;

        if (!isPickerStyleCall)
        {
            var legacy = await BuildLegacyBrowsePageResultAsync(scope is null ? null : scope with { NativeFormat = null }, cancellationToken);
            return query.Sorting is null ? legacy : legacy with { Items = query.Sorting.Order(legacy.Items, p => p.Name, p => p.Id).ToList() };
        }

        // US2 picker path (research.md §6) — the DB/repository already narrowed and paginated the
        // rows, so lean fields are returned directly with no per-row ancestor-chain merge, keeping
        // this responsive at a vendor's full ingested scale (SC-010) regardless of total catalog size.
        var page = query.Page ?? 1;
        var (rows, totalCount) = await repository.SearchAsync(
            ProfileOriginQueryParser.ParseEffectiveFilter(query.Origin), true, null,
            null, query.SearchText, page, query.PageSize ?? 100_000, cancellationToken, query.Sorting, scope);

        var leanSummaries = rows.Select(r => new MachineProfileSummary(
            r.Id, r.Name, r.Origin, r.VendorName, r.SourceFormat, r.BaseProfileId, r.BaseProfileName,
            false, null, null, null, null, null, [])
        { SourceVersion = r.SourceVersion }).ToList();

        // "Omit page" means one page containing everything (contracts/profiles-api.md) — the
        // reported pageSize then reflects the actual row count, not the internal unbounded cap.
        var reportedPageSize = query.PageSize ?? totalCount;

        return new ProfilePage<MachineProfileSummary>(leanSummaries, page, reportedPageSize, totalCount);
    }

    // Feature 003, FR-010/US3's original browse page, restricted to User rows only now that System
    // rows exist in the same table (009 FR-013 — System profiles are picker-only, never browsable/
    // importable here). Each User row's chain is walked by id, so it resolves through a System
    // ancestor of ANY catalog version (a User row pins the ancestor it was imported against).
    private async Task<ProfilePage<MachineProfileSummary>> BuildLegacyBrowsePageResultAsync(ProfileCatalogScope? scope, CancellationToken cancellationToken)
    {
        var userRows = (await repository.ListAsync(cancellationToken, scope)).Where(r => r.Origin == ProfileOrigin.User).ToList();

        var summaries = new List<MachineProfileSummary>(userRows.Count);

        foreach (var row in userRows)
        {
            var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(row.Id, FetchNode, cancellationToken);
            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            var fields = parser.ExtractMachineDisplayFields(merged, row.SourceFormat);

            summaries.Add(new MachineProfileSummary(
                row.Id, row.Name, row.Origin, row.VendorName, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.NozzleSizeMm, fields.BedSizeXMm, fields.BedSizeYMm, fields.MaxHeightMm, fields.HasEnclosure, fields.SupportedMaterials)
            { SourceVersion = row.SourceVersion });
        }

        return new ProfilePage<MachineProfileSummary>(summaries, 1, summaries.Count, summaries.Count);
    }

    private async Task<ProfileChainNode?> FetchNode(Guid id, CancellationToken cancellationToken)
    {
        var node = await repository.GetByIdAsync(id, cancellationToken);
        return node is null ? null : new ProfileChainNode(node.RawDataJson, node.BaseProfileId, node.BaseProfileName, node.Origin, node.SourceVersion);
    }
}
