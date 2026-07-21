using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public sealed record GetMachineProfileQuery(Guid Id);

public sealed record MachineProfileDetail(
    Guid Id,
    string Name,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    string RawDataJson,
    DateTimeOffset CreatedAtUtc,
    double? NozzleSizeMm,
    double? BedSizeXMm,
    double? BedSizeYMm,
    double? MaxHeightMm,
    bool? HasEnclosure,
    IReadOnlyList<string> SupportedMaterials,
    Guid? DefaultProcessProfileId,
    string? DefaultProcessProfileName);

// The row's own rawDataJson (FR-011) plus chain-merged derived fields via sequential bounded
// fetch-by-id up BaseProfileId (research.md §10) — not an N+1 pattern, one bounded chain per request.
public sealed class GetMachineProfileQueryHandler(
    IMachineProfileRepository repository, IProcessProfileRepository processProfileRepository, IProfileBundleParser parser)
    : IQueryHandler<GetMachineProfileQuery, MachineProfileDetail?>
{
    public async Task<MachineProfileDetail?> HandleAsync(GetMachineProfileQuery query, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(
            row.Id, FetchNode, cancellationToken);
        var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
        var fields = parser.ExtractMachineDisplayFields(merged, row.SourceFormat);

        // Tolerates a dangling DefaultProcessProfileId (e.g. the paired row was somehow removed) by
        // degrading to null rather than failing the whole detail read — same philosophy as this
        // handler's own ancestor-chain resolution above.
        string? defaultProcessProfileName = null;
        if (row.DefaultProcessProfileId is { } processProfileId)
        {
            var processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken);
            defaultProcessProfileName = processProfile?.Name;
        }

        return new MachineProfileDetail(
            row.Id, row.Name, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
            row.RawDataJson, row.CreatedAtUtc,
            fields.NozzleSizeMm, fields.BedSizeXMm, fields.BedSizeYMm, fields.MaxHeightMm, fields.HasEnclosure, fields.SupportedMaterials,
            row.DefaultProcessProfileId, defaultProcessProfileName);
    }

    private async Task<ProfileChainNode?> FetchNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }
}
