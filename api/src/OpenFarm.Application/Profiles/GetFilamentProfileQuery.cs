using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

public sealed record GetFilamentProfileQuery(Guid Id);

public sealed record FilamentProfileDetail(
    Guid Id,
    string Name,
    ProfileSourceFormat SourceFormat,
    Guid? BaseProfileId,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    string RawDataJson,
    DateTimeOffset CreatedAtUtc,
    string? Material,
    string? Brand,
    string? ColorHex,
    double? NozzleSizeMm,
    double? NozzleTempC,
    double? BedTempC,
    double? MaxVolumetricSpeedMm3s,
    int? FanPercent,
    IReadOnlyList<string> CompatiblePrinterNames);

public sealed class GetFilamentProfileQueryHandler(IFilamentProfileRepository repository, IProfileBundleParser parser)
    : IQueryHandler<GetFilamentProfileQuery, FilamentProfileDetail?>
{
    public async Task<FilamentProfileDetail?> HandleAsync(GetFilamentProfileQuery query, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(
            row.Id, FetchNode, cancellationToken);
        var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
        var fields = parser.ExtractFilamentDisplayFields(merged, row.SourceFormat, row.Name, hasBaseProfileName: row.BaseProfileName is not null);

        return new FilamentProfileDetail(
            row.Id, row.Name, row.SourceFormat, row.BaseProfileId, row.BaseProfileName, hasUnresolvedBase,
            row.RawDataJson, row.CreatedAtUtc,
            fields.Material, fields.Brand, fields.ColorHex, fields.NozzleSizeMm, fields.NozzleTempC, fields.BedTempC,
            fields.MaxVolumetricSpeedMm3s, fields.FanPercent, fields.CompatiblePrinterNames);
    }

    private async Task<ProfileChainNode?> FetchNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }
}
