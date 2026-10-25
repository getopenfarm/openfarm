using System.IO.Compression;
using System.Text.RegularExpressions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Fixture-replay, test-after (research.md §12/§13) — reads an unsliced 3MF's own
// Metadata/project_settings.config (the OrcaSlicer/Bambu Studio project-settings JSON entry every
// saved project contains, distinct from the sliced-only Metadata/slice_info.config) for its
// filament-preset assignment, plus Metadata/model_settings.config's <plate> elements for plate count.
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

            var plateCount = 1;
            string? modelSettingsXml = null;
            var modelSettingsEntry = SlicedPlateMetadata.Find(archive, "Metadata/model_settings.config");
            if (modelSettingsEntry is not null)
            {
                using var reader = new StreamReader(modelSettingsEntry.Open());
                var xml = modelSettingsXml = await reader.ReadToEndAsync(cancellationToken);
                var matches = PlateElementRegex().Count(xml);
                if (matches > 0)
                {
                    plateCount = matches;
                }
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
            if (metadata is { MaterialComplete: true } && modelSettingsXml is not null
                && ProjectUsedFilaments.Read(modelSettingsXml, HasPaintedColours(archive)) is { } used
                && used.All(i => i < metadata.Slots.Count))
            {
                var order = used.Order().ToList();
                slots = order.Select(i => metadata.Slots[i]).ToList();
                if (details?.FilamentProfileNames is { } names && names.Count == metadata.Slots.Count)
                    details = details with { FilamentProfileNames = order.Select(i => names[i]).ToList() };
            }

            return new UnslicedProjectMaterial(slots is { Count: > 0 } ? slots[0].MaterialType : null, plateCount, thumbnails,
                settingsEntry is not null || unsupported, metadata?.Nozzle, details,
                metadata?.UnavailableReason ?? (unsupported ? "Estimation unavailable: this embedded project format is not supported." : null),
                slots, metadata?.MaterialComplete ?? true);
        }
    }

    /// <summary>Whether any object volume paints colours (a slot per triangle), which the settings alone cannot account for.</summary>
    private static bool HasPaintedColours(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(e => PaintableModelRegex().IsMatch(e.FullName.Replace('\\', '/'))))
        {
            using var reader = new StreamReader(entry.Open());
            var buffer = new char[81920];
            var carry = string.Empty;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Overlap chunks so an attribute name split across two reads is still seen.
                var text = carry + new string(buffer, 0, read);
                if (text.Contains("paint_color", StringComparison.Ordinal) || text.Contains("mmu_segmentation", StringComparison.Ordinal)) return true;
                carry = text[^Math.Min(text.Length, 16)..];
            }
        }

        return false;
    }

    [GeneratedRegex(@"^3D/(?:Objects/.+|3dmodel)\.model$", RegexOptions.IgnoreCase)]
    private static partial Regex PaintableModelRegex();

    [GeneratedRegex(@"^Metadata/plate_(\d+)\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex PlateThumbnailRegex();

    [GeneratedRegex(@"<plate\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlateElementRegex();
}
