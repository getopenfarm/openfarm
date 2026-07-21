using OpenFarm.Application.Common;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Parses the upload via IProfileBundleParser, flags duplicates (research.md §5), resolves each
// preset's base chain by (SourceFormat, Name) against same-batch siblings first, then
// prior-imported rows, merging via ProfileRawDataMerger for derived display fields and
// hasUnresolvedBaseInChain (research.md §10) — no persistence.
public sealed class PreviewProfileImportCommandHandler(
    IProfileBundleParser parser,
    IMachineProfileRepository machineProfileRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IProcessProfileRepository processProfileRepository)
    : ICommandHandler<PreviewProfileImportCommand, PreviewProfileImportResult>
{
    private const int MaxChainDepth = 32;

    public async Task<PreviewProfileImportResult> HandleAsync(PreviewProfileImportCommand command, CancellationToken cancellationToken)
    {
        var sourceFormat = ProfileBundleFormatResolver.Resolve(command.FileName);
        var bundle = await parser.ParseAsync(command.FileStream, sourceFormat, cancellationToken);

        var machineViews = new List<ParsedMachinePresetView>();
        foreach (var preset in bundle.MachinePresets)
        {
            var isDuplicate = (await machineProfileRepository.FindByNameAsync(sourceFormat, preset.Name, cancellationToken)).Count > 0;
            var (mergedJson, hasUnresolvedBase) = await ResolveMachineChainAsync(preset, bundle.MachinePresets, sourceFormat, cancellationToken);
            var fields = parser.ExtractMachineDisplayFields(mergedJson, sourceFormat);

            machineViews.Add(new ParsedMachinePresetView(
                preset.Name, sourceFormat, preset.BaseProfileName, hasUnresolvedBase, isDuplicate,
                fields.NozzleSizeMm, fields.BedSizeXMm, fields.BedSizeYMm, fields.MaxHeightMm, fields.HasEnclosure, fields.SupportedMaterials));
        }

        var filamentViews = new List<ParsedFilamentPresetView>();
        foreach (var preset in bundle.FilamentPresets)
        {
            var isDuplicate = (await filamentProfileRepository.FindByNameAsync(sourceFormat, preset.Name, cancellationToken)).Count > 0;
            var (mergedJson, hasUnresolvedBase) = await ResolveFilamentChainAsync(preset, bundle.FilamentPresets, sourceFormat, cancellationToken);
            var fields = parser.ExtractFilamentDisplayFields(mergedJson, sourceFormat, preset.Name, hasBaseProfileName: preset.BaseProfileName is not null);

            filamentViews.Add(new ParsedFilamentPresetView(
                preset.Name, sourceFormat, preset.BaseProfileName, hasUnresolvedBase, isDuplicate,
                fields.Material, fields.Brand, fields.ColorHex, fields.NozzleSizeMm, fields.NozzleTempC, fields.BedTempC,
                fields.MaxVolumetricSpeedMm3s, fields.FanPercent, fields.CompatiblePrinterNames));
        }

        var processViews = new List<ParsedProcessPresetView>();
        foreach (var preset in bundle.ProcessPresets)
        {
            var isDuplicate = (await processProfileRepository.FindByNameAsync(sourceFormat, preset.Name, cancellationToken)).Count > 0;
            var (mergedJson, hasUnresolvedBase) = await ResolveProcessChainAsync(preset, bundle.ProcessPresets, sourceFormat, cancellationToken);
            var fields = parser.ExtractProcessDisplayFields(mergedJson, sourceFormat);

            processViews.Add(new ParsedProcessPresetView(
                preset.Name, sourceFormat, preset.BaseProfileName, hasUnresolvedBase, isDuplicate,
                fields.LayerHeightMm, fields.WallLoops, fields.CompatiblePrinterNames));
        }

        return new PreviewProfileImportResult(machineViews, filamentViews, processViews);
    }

    // Name-matched chain walk (research.md §10's preview-side variant of §3's resolution order):
    // same-batch siblings first, then prior-imported catalog rows (whose own persisted chain is
    // walked id-matched via ProfileChainWalker). Ambiguous or no match at either step stops the walk
    // and flags hasUnresolvedBaseInChain, mirroring Pass 2's "unresolved is a normal outcome" handling.
    private async Task<(string MergedJson, bool HasUnresolvedBase)> ResolveMachineChainAsync(
        ParsedMachinePreset preset, IReadOnlyList<ParsedMachinePreset> batch, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        var ancestorsOldestFirst = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var hasUnresolvedBase = false;
        var baseName = preset.BaseProfileName;
        var depth = 0;

        while (baseName is not null)
        {
            if (depth++ >= MaxChainDepth || !visited.Add(baseName))
            {
                hasUnresolvedBase = true;
                break;
            }

            var batchMatches = batch.Where(p => p.Name == baseName).ToList();
            if (batchMatches.Count == 1)
            {
                ancestorsOldestFirst.Insert(0, batchMatches[0].RawDataJson);
                baseName = batchMatches[0].BaseProfileName;
                continue;
            }

            if (batchMatches.Count > 1)
            {
                hasUnresolvedBase = true;
                break;
            }

            var catalogMatches = await machineProfileRepository.FindByNameAsync(sourceFormat, baseName, cancellationToken);
            if (catalogMatches.Count == 1)
            {
                var (rowAncestors, rowUnresolved) = await ProfileChainWalker.WalkByIdAsync(
                    catalogMatches[0].Id, FetchMachineNode, cancellationToken);
                ancestorsOldestFirst.InsertRange(0, rowAncestors);
                hasUnresolvedBase = hasUnresolvedBase || rowUnresolved;
            }
            else
            {
                hasUnresolvedBase = true;
            }

            break;
        }

        ancestorsOldestFirst.Add(preset.RawDataJson);
        return (ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst), hasUnresolvedBase);
    }

    private async Task<(string MergedJson, bool HasUnresolvedBase)> ResolveFilamentChainAsync(
        ParsedFilamentPreset preset, IReadOnlyList<ParsedFilamentPreset> batch, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        var ancestorsOldestFirst = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var hasUnresolvedBase = false;
        var baseName = preset.BaseProfileName;
        var depth = 0;

        while (baseName is not null)
        {
            if (depth++ >= MaxChainDepth || !visited.Add(baseName))
            {
                hasUnresolvedBase = true;
                break;
            }

            var batchMatches = batch.Where(p => p.Name == baseName).ToList();
            if (batchMatches.Count == 1)
            {
                ancestorsOldestFirst.Insert(0, batchMatches[0].RawDataJson);
                baseName = batchMatches[0].BaseProfileName;
                continue;
            }

            if (batchMatches.Count > 1)
            {
                hasUnresolvedBase = true;
                break;
            }

            var catalogMatches = await filamentProfileRepository.FindByNameAsync(sourceFormat, baseName, cancellationToken);
            if (catalogMatches.Count == 1)
            {
                var (rowAncestors, rowUnresolved) = await ProfileChainWalker.WalkByIdAsync(
                    catalogMatches[0].Id, FetchFilamentNode, cancellationToken);
                ancestorsOldestFirst.InsertRange(0, rowAncestors);
                hasUnresolvedBase = hasUnresolvedBase || rowUnresolved;
            }
            else
            {
                hasUnresolvedBase = true;
            }

            break;
        }

        ancestorsOldestFirst.Add(preset.RawDataJson);
        return (ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst), hasUnresolvedBase);
    }

    private async Task<(string MergedJson, bool HasUnresolvedBase)> ResolveProcessChainAsync(
        ParsedProcessPreset preset, IReadOnlyList<ParsedProcessPreset> batch, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken)
    {
        var ancestorsOldestFirst = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var hasUnresolvedBase = false;
        var baseName = preset.BaseProfileName;
        var depth = 0;

        while (baseName is not null)
        {
            if (depth++ >= MaxChainDepth || !visited.Add(baseName))
            {
                hasUnresolvedBase = true;
                break;
            }

            var batchMatches = batch.Where(p => p.Name == baseName).ToList();
            if (batchMatches.Count == 1)
            {
                ancestorsOldestFirst.Insert(0, batchMatches[0].RawDataJson);
                baseName = batchMatches[0].BaseProfileName;
                continue;
            }

            if (batchMatches.Count > 1)
            {
                hasUnresolvedBase = true;
                break;
            }

            var catalogMatches = await processProfileRepository.FindByNameAsync(sourceFormat, baseName, cancellationToken);
            if (catalogMatches.Count == 1)
            {
                var (rowAncestors, rowUnresolved) = await ProfileChainWalker.WalkByIdAsync(
                    catalogMatches[0].Id, FetchProcessNode, cancellationToken);
                ancestorsOldestFirst.InsertRange(0, rowAncestors);
                hasUnresolvedBase = hasUnresolvedBase || rowUnresolved;
            }
            else
            {
                hasUnresolvedBase = true;
            }

            break;
        }

        ancestorsOldestFirst.Add(preset.RawDataJson);
        return (ProfileRawDataMerger.MergeAncestorChain(ancestorsOldestFirst), hasUnresolvedBase);
    }

    private async Task<ProfileChainNode?> FetchProcessNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await processProfileRepository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }

    private async Task<ProfileChainNode?> FetchMachineNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await machineProfileRepository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }

    private async Task<ProfileChainNode?> FetchFilamentNode(Guid id, CancellationToken cancellationToken)
    {
        var row = await filamentProfileRepository.GetByIdAsync(id, cancellationToken);
        return row is null ? null : new ProfileChainNode(row.RawDataJson, row.BaseProfileId, row.BaseProfileName);
    }
}
