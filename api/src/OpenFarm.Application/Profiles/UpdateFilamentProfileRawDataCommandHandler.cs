using OpenFarm.Application.Common;

namespace OpenFarm.Application.Profiles;

// FR-012/FR-013 — same behavior as UpdateMachineProfileRawDataCommandHandler, scoped to FilamentProfile.
public sealed class UpdateFilamentProfileRawDataCommandHandler(IFilamentProfileRepository repository, IProfileBundleParser parser)
    : ICommandHandler<UpdateFilamentProfileRawDataCommand, FilamentProfileDetail>
{
    public async Task<FilamentProfileDetail> HandleAsync(UpdateFilamentProfileRawDataCommand command, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(command.FilamentProfileId, cancellationToken)
            ?? throw new FilamentProfileNotFoundException(command.FilamentProfileId);

        ProfileRawJson.EnsureEditable(row.Origin, command.RawDataJson, "Filament profile");

        row.ReplaceRawData(command.RawDataJson);
        await repository.SaveChangesAsync(cancellationToken);

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
        var node = await repository.GetByIdAsync(id, cancellationToken);
        return node is null ? null : new ProfileChainNode(node.RawDataJson, node.BaseProfileId, node.BaseProfileName);
    }
}
