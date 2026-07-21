using System.Text.Json;
using OpenFarm.Application.Common;

namespace OpenFarm.Application.Profiles;

// FR-012/FR-013 — overwrites RawDataJson only (data-model.md: no re-derivation step to keep in
// sync); a parse failure throws InvalidProfileJsonException before the row is touched, leaving the
// previously saved value unchanged (US4 Acceptance Scenario 2).
public sealed class UpdateMachineProfileRawDataCommandHandler(
    IMachineProfileRepository repository, IProcessProfileRepository processProfileRepository, IProfileBundleParser parser)
    : ICommandHandler<UpdateMachineProfileRawDataCommand, MachineProfileDetail>
{
    public async Task<MachineProfileDetail> HandleAsync(UpdateMachineProfileRawDataCommand command, CancellationToken cancellationToken)
    {
        var row = await repository.GetByIdAsync(command.MachineProfileId, cancellationToken)
            ?? throw new MachineProfileNotFoundException(command.MachineProfileId);

        try
        {
            JsonDocument.Parse(command.RawDataJson).Dispose();
        }
        catch (JsonException ex)
        {
            throw new InvalidProfileJsonException($"Machine profile raw data is not valid JSON: {ex.Message}");
        }

        row.ReplaceRawData(command.RawDataJson);
        await repository.SaveChangesAsync(cancellationToken);

        var (ancestorsOldestFirst, hasUnresolvedBase) = await ProfileChainWalker.WalkByIdAsync(
            row.Id, FetchNode, cancellationToken);
        var merged = ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst);
        var fields = parser.ExtractMachineDisplayFields(merged, row.SourceFormat);

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
        var node = await repository.GetByIdAsync(id, cancellationToken);
        return node is null ? null : new ProfileChainNode(node.RawDataJson, node.BaseProfileId, node.BaseProfileName);
    }
}
