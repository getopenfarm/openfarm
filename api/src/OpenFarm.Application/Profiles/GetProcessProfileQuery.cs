using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public sealed record GetProcessProfileQuery(Guid Id);

public sealed record ProcessProfileDetail(
    Guid Id,
    string Name,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    string RawDataJson,
    DateTimeOffset CreatedAtUtc,
    double? LayerHeightMm,
    int? WallLoops,
    IReadOnlyList<string> CompatiblePrinterNames);

public sealed class GetProcessProfileQueryHandler(IProcessProfileRepository repository, IProfileBundleParser parser)
    : IQueryHandler<GetProcessProfileQuery, ProcessProfileDetail?>
{
    public async Task<ProcessProfileDetail?> HandleAsync(GetProcessProfileQuery query, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(
            row.Id, FetchNode, cancellationToken);
        var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
        var fields = parser.ExtractProcessDisplayFields(merged, row.SourceFormat);

        return new ProcessProfileDetail(
            row.Id, row.Name, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
            row.RawDataJson, row.CreatedAtUtc,
            fields.LayerHeightMm, fields.WallLoops, fields.CompatiblePrinterNames);
    }

    private async Task<ProfileChainNode?> FetchNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }
}
