using OpenFarm.Application.Common;
using System.Text.Json;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Profiles;

// All fields null/omitted reproduces feature 003's exact original call (FR-013) — every other
// combination is the US2/US4 picker's paginated/searchable path (research.md §6).
public sealed record GetFilamentProfilesQuery(
    string? Origin = null,
    string? SearchText = null,
    string? CompatibleWithMachineName = null,
    string? MaterialType = null,
    int? Page = null,
    int? PageSize = null, string? Brand = null, string? ModelCode = null, decimal? NozzleDiameterMm = null, ListSort? Sorting = null);

public sealed record FilamentProfileSummary(
    Guid Id,
    string Name,
    ProfileOrigin Origin,
    string? VendorName,
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
    IReadOnlyList<string> CompatiblePrinterNames)
{
    // Domain-level canonical field (set at ingestion, research.md §0), distinct from the
    // parsed/display-only `Material` above which only ever gets populated for User rows.
    public string? MaterialType { get; init; }

    /// <summary>The slicer version this System row belongs to; <c>null</c> for User rows.</summary>
    public string? SourceVersion { get; init; }
}

public sealed class GetFilamentProfilesQueryHandler(IFilamentProfileRepository repository, IProfileBundleParser parser, ProfileCompatibility compatibility,
    IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults> slicingDefaults, ProfileCatalogVersions? catalogVersions = null)
    : IQueryHandler<GetFilamentProfilesQuery, ProfilePage<FilamentProfileSummary>>
{
    public async Task<ProfilePage<FilamentProfileSummary>> HandleAsync(GetFilamentProfilesQuery query, CancellationToken cancellationToken)
    {
        var scope = catalogVersions is null ? null : await catalogVersions.GetScopeAsync(cancellationToken);
        if (query.MaterialType is not null || query.Brand is not null)
        {
            var engine = query.Brand is not null ? (await slicingDefaults.HandleAsync(new(), cancellationToken)).Default?.Engine : null;
            var matches = new List<FilamentProfileSummary>();
            foreach (var profile in await repository.ListAsync(cancellationToken, scope))
            {
                if (!profile.IsSelectable || !profile.IsCurrentlyOffered
                    || engine is { } selectedEngine && !ProfileEngineCompatibility.Supports(selectedEngine, profile.SourceFormat)
                    || query.Origin is { } origin && origin != "All" && profile.Origin.ToString() != origin
                    || !string.IsNullOrWhiteSpace(query.SearchText) && !profile.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var merged = await compatibility.FilamentJsonAsync(profile, cancellationToken);
                    using var json = JsonDocument.Parse(merged);
                    var materials = ProfileCompatibility.ReadStrings(json.RootElement, "filament_type");
                    var material = materials.Count > 0 ? materials[0] : null;
                    if (query.MaterialType is not null && material != query.MaterialType) continue;
                    if (query.Brand is not null)
                    {
                        if (query.ModelCode is null || query.NozzleDiameterMm is not > 0) continue;
                        await compatibility.ValidateFilamentAsync(profile, material ?? "", query.Brand, query.ModelCode,
                            query.NozzleDiameterMm.Value, cancellationToken, scope: scope);
                    }
                    var names = ProfileCompatibility.ReadStrings(json.RootElement, "compatible_printers");
                    if (query.CompatibleWithMachineName is { } machineName && names.Count > 0 && !names.Contains(machineName, StringComparer.Ordinal)) continue;
                    var fields = parser.ExtractFilamentDisplayFields(merged, profile.SourceFormat, profile.Name, profile.BaseProfileName is not null);
                    matches.Add(new(profile.Id, profile.Name, profile.Origin, profile.VendorName, profile.SourceFormat, profile.BaseProfileId,
                        profile.BaseProfileName, false, fields.Material, fields.Brand, fields.ColorHex, fields.NozzleSizeMm, fields.NozzleTempC,
                        fields.BedTempC, fields.MaxVolumetricSpeedMm3s, fields.FanPercent, names)
                    { MaterialType = material, SourceVersion = profile.SourceVersion });
                }
                catch (SlicingProfileUnconfiguredException) { }
            }
            var size = Math.Clamp(query.PageSize ?? 50, 1, 1000);
            var requestedPage = Math.Max(query.Page ?? 1, 1);
            var ordered = query.Sorting is null
                ? matches.OrderBy(p => engine is { } selected && ProfileEngineCompatibility.NativeEngine(p.SourceFormat) != selected)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id)
                : query.Sorting.Order(matches, p => p.Name, p => p.Id);
            return new(ordered.Skip((requestedPage - 1) * size).Take(size).ToList(), requestedPage, size, matches.Count);
        }
        var isPickerStyleCall = query.Origin is not null || query.SearchText is not null
            || query.CompatibleWithMachineName is not null || query.MaterialType is not null
            || query.Page is not null || query.PageSize is not null;

        if (!isPickerStyleCall)
        {
            var legacy = await BuildLegacyBrowsePageResultAsync(scope is null ? null : scope with { NativeFormat = null }, cancellationToken);
            return query.Sorting is null ? legacy : legacy with { Items = query.Sorting.Order(legacy.Items, p => p.Name, p => p.Id).ToList() };
        }

        var page = query.Page ?? 1;
        var (rows, totalCount) = await repository.SearchAsync(
            ProfileOriginQueryParser.ParseEffectiveFilter(query.Origin), true, query.CompatibleWithMachineName,
            query.MaterialType, query.SearchText, page, query.PageSize ?? 100_000, cancellationToken, query.Sorting, scope);

        var leanSummaries = rows.Select(r => new FilamentProfileSummary(
            r.Id, r.Name, r.Origin, r.VendorName, r.SourceFormat, r.BaseProfileId, r.BaseProfileName,
            false, null, null, null, null, null, null, null, null, [])
        {
            MaterialType = r.MaterialType,
            SourceVersion = r.SourceVersion,
        }).ToList();

        var reportedPageSize = query.PageSize ?? totalCount;

        return new ProfilePage<FilamentProfileSummary>(leanSummaries, page, reportedPageSize, totalCount);
    }

    // Feature 003, FR-010/US3's original browse page, restricted to User rows only now that System
    // rows exist in the same table (009 FR-013 — System profiles are picker-only, never browsable/
    // importable here). Each User row's chain is walked by id, so it resolves through a System
    // ancestor of ANY catalog version (a User row pins the ancestor it was imported against).
    private async Task<ProfilePage<FilamentProfileSummary>> BuildLegacyBrowsePageResultAsync(ProfileCatalogScope? scope, CancellationToken cancellationToken)
    {
        var userRows = (await repository.ListAsync(cancellationToken, scope)).Where(r => r.Origin == ProfileOrigin.User).ToList();

        var summaries = new List<FilamentProfileSummary>(userRows.Count);

        foreach (var row in userRows)
        {
            var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(row.Id, FetchNode, cancellationToken);
            var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
            var fields = parser.ExtractFilamentDisplayFields(merged, row.SourceFormat, row.Name, hasBaseProfileName: row.BaseProfileName is not null);

            summaries.Add(new FilamentProfileSummary(
                row.Id, row.Name, row.Origin, row.VendorName, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
                fields.Material, fields.Brand, fields.ColorHex, fields.NozzleSizeMm, fields.NozzleTempC, fields.BedTempC,
                fields.MaxVolumetricSpeedMm3s, fields.FanPercent, fields.CompatiblePrinterNames)
            {
                MaterialType = row.MaterialType,
                SourceVersion = row.SourceVersion,
            });
        }

        return new ProfilePage<FilamentProfileSummary>(summaries, 1, summaries.Count, summaries.Count);
    }

    private async Task<ProfileChainNode?> FetchNode(Guid id, CancellationToken cancellationToken)
    {
        var node = await repository.GetByIdAsync(id, cancellationToken);
        return node is null ? null : new ProfileChainNode(node.RawDataJson, node.BaseProfileId, node.BaseProfileName, node.Origin, node.SourceVersion);
    }
}
