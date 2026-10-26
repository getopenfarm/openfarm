using System.IO.Compression;
using System.Text.RegularExpressions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Fixture-replay, test-after (research.md §12/§13) — reads an unsliced 3MF's own
// Metadata/project_settings.config (the OrcaSlicer/Bambu Studio project-settings JSON entry every
// saved project contains, distinct from the sliced-only Metadata/slice_info.config) for its
// filament-preset assignment, plus Metadata/model_settings.config's <plate> elements for its plates.
public sealed partial class UnslicedProjectMaterialResolver : IUnslicedProjectMaterialResolver
{
    public async Task<UnslicedProjectMaterial> ResolveAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IOException or EndOfStreamException)
        {
            throw new UnslicedProjectMaterialParsingFailedException("The uploaded 3MF could not be read as a valid zip archive.");
        }

        using (archive)
        {
            var settingsEntry = SlicedPlateMetadata.Find(archive, "Metadata/project_settings.config");
            ProjectSettingsMetadata? metadata = null;
            if (settingsEntry is not null)
            {
                using var reader = new StreamReader(settingsEntry.Open());
                metadata = ProjectSettingsMetadata.Read(await reader.ReadToEndAsync(cancellationToken));
            }

            string? modelSettingsXml = null;
            var modelSettingsEntry = SlicedPlateMetadata.Find(archive, "Metadata/model_settings.config");
            if (modelSettingsEntry is not null)
            {
                using var reader = new StreamReader(modelSettingsEntry.Open());
                modelSettingsXml = await reader.ReadToEndAsync(cancellationToken);
            }

            var thumbnails = new List<ThumbnailSpec>();
            foreach (var entry in archive.Entries)
            {
                var match = PlateThumbnailRegex().Match(entry.FullName.Replace('\\', '/'));
                if (!match.Success || !int.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 1) continue;
                using var input = entry.Open();
                using var buffer = new MemoryStream();
                await input.CopyToAsync(buffer, cancellationToken);
                thumbnails.Add(new ThumbnailSpec(number - 1, buffer.ToArray()));
            }
            var unsupported = archive.Entries.Any(e => e.FullName.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)
                && (e.FullName.Contains("Slic3r_PE.config", StringComparison.OrdinalIgnoreCase)
                    || e.FullName.Contains("Cura", StringComparison.OrdinalIgnoreCase)));
            // A project lists every filament slot it defines; only the ones its objects print with are requirements. Slots
            // line up with the project's arrays only when none is blank (MaterialComplete), so otherwise keep them all.
            var slots = metadata?.Slots;
            var details = metadata?.Details;
            var painted = ProjectUsedFilaments.PaintedSlots(archive);
            // The slots some objects print with, in slot order; null keeps them all (see ProjectUsedFilaments.Read).
            IReadOnlyList<int>? UsedSlots(IReadOnlySet<string>? objectIds) =>
                metadata is { MaterialComplete: true } && modelSettingsXml is not null
                && ProjectUsedFilaments.Read(modelSettingsXml, painted, objectIds) is { } used && used.All(i => i < metadata.Slots.Count)
                    ? used.Order().ToList() : null;
            if (UsedSlots(null) is { } order)
            {
                slots = order.Select(i => metadata!.Slots[i]).ToList();
                if (details?.FilamentProfileNames is { } names && names.Count == metadata!.Slots.Count)
                    details = details with { FilamentProfileNames = order.Select(i => names[i]).ToList() };
            }

            // Plates with objects on them, each with the slots its own objects print with; a project without any is one plate.
            var projectPlates = modelSettingsXml is null ? [] : ProjectUsedFilaments.Plates(modelSettingsXml).Where(p => p.ObjectIds.Count > 0).ToList();
            IReadOnlyList<FilePlate> plates = projectPlates.Count == 0
                ? [new FilePlate(1, null, slots ?? [])]
                : projectPlates.Select(p => new FilePlate(p.Number, p.Name,
                    UsedSlots(p.ObjectIds) is { } own ? own.Select(i => metadata!.Slots[i]).ToList() : slots ?? [])).ToList();

            return new UnslicedProjectMaterial(slots is { Count: > 0 } ? slots[0].MaterialType : null, plates, thumbnails,
                settingsEntry is not null || unsupported, metadata?.Nozzle, details,
                metadata?.UnavailableReason ?? (unsupported ? "Estimation unavailable: this embedded project format is not supported." : null),
                slots, metadata?.MaterialComplete ?? true);
        }
    }

    [GeneratedRegex(@"^Metadata/plate_(\d+)\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex PlateThumbnailRegex();
}
