using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using OpenFarm.Application.Parts;
using OpenFarm.Infrastructure.Production;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Fixture-replay, test-after (research.md §12, mirrors ProfileBundleParser/BambuMqttMessageParser's
// existing carve-out) — reads a standalone G-code's header/footer comments, or walks every plate's
// own embedded G-code inside a .gcode.3mf (Metadata/plate_<n>.gcode + Metadata/plate_<n>.png),
// reporting each plate on its own and summing estimates and filament requirements across them (research.md §7).
public sealed partial class AlreadySlicedFileMetadataParser : IAlreadySlicedFileMetadataParser
{
    public async Task<SlicedFileMetadata> ParseAsync(Stream fileStream, PartFileFormat format, CancellationToken cancellationToken)
    {
        return format switch
        {
            PartFileFormat.GCode => await ParseGCodeAsync(fileStream, cancellationToken),
            PartFileFormat.ThreeMf => await ParseGCode3mfAsync(fileStream, cancellationToken),
            _ => throw new PartFileParsingFailedException($"'{format}' is not an already-sliced format."),
        };
    }

    private static async Task<SlicedFileMetadata> ParseGCodeAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(fileStream, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);

        var plate = ParsePlate(content);
        var grams = Grams([ReadUsage(() => JobFilamentUsageReader.Read(System.Text.Encoding.UTF8.GetBytes(content), 1))]);

        return new SlicedFileMetadata(
            plate.NozzleDiameterMm,
            plate.EstimatedPrintTimeMinutes,
            plate.EstimatedFilamentWeightGrams,
            [new FilePlate(1, null, plate.FilamentSlots, plate.EstimatedPrintTimeMinutes, plate.EstimatedFilamentWeightGrams, grams)],
            plate.FilamentSlots,
            plate.Thumbnail is null ? [] : [new ThumbnailSpec(0, plate.Thumbnail)],
            plate.MaterialMetadataComplete, DescribeGCode(content),
            FilamentGrams: grams);
    }

    private static async Task<SlicedFileMetadata> ParseGCode3mfAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IOException or EndOfStreamException)
        {
            throw new PartFileParsingFailedException("The uploaded 3MF could not be read as a valid zip archive.");
        }

        using (archive)
        {
            var plateNumbers = archive.Entries
                .Select(e => PlateGCodeEntryRegex().Match(e.FullName.Replace('\\', '/')))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (plateNumbers.Count == 0)
            {
                throw new PartFileParsingFailedException("No per-plate G-code was found inside the 3MF's Metadata folder.");
            }

            decimal? nozzleDiameterMm = null;
            int? totalMinutes = 0;
            decimal? totalWeightGrams = 0m;
            var materialComplete = true;
            var nozzleComplete = true;
            var allSlots = new List<DetectedFilamentSlot>();
            var thumbnails = new List<ThumbnailSpec>();
            var warnings = new List<string>();
            ProjectSettingsMetadata? project = null;
            if (SlicedPlateMetadata.Find(archive, "Metadata/project_settings.config") is { } projectEntry)
            {
                using var reader = new StreamReader(projectEntry.Open());
                project = ProjectSettingsMetadata.Read(await reader.ReadToEndAsync(cancellationToken));
            }
            EstimationDetails? details = project is null ? null : project.Details with { Source = "EmbeddedSlicedMetadata" };
            IReadOnlyList<ProjectUsedFilaments.ProjectPlate> names = [];
            if (SlicedPlateMetadata.Find(archive, "Metadata/model_settings.config") is { } modelSettings)
            {
                using var reader = new StreamReader(modelSettings.Open());
                names = ProjectUsedFilaments.Plates(await reader.ReadToEndAsync(cancellationToken));
            }
            var plates = new List<FilePlate>();
            var usages = new List<JobFilamentUsageReport?>();

            foreach (var plateNumber in plateNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var gcodeEntry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'),
                    $"Metadata/plate_{plateNumber}.gcode", StringComparison.OrdinalIgnoreCase));
                if (gcodeEntry is null)
                {
                    continue;
                }

                string gcodeContent;
                using (var entryReader = new StreamReader(gcodeEntry.Open()))
                {
                    gcodeContent = await entryReader.ReadToEndAsync(cancellationToken);
                }

                // Indexed by plate number, like an unsliced project's thumbnails, so a plate keeps its thumbnail across the two.
                var plateIndex = plateNumber - 1;
                var thumbnailEntry = archive.GetEntry($"Metadata/plate_{plateNumber}.png");
                var parsed = ParsePlate(gcodeContent, thumbnailEntry);
                var gcodeDetails = DescribeGCode(gcodeContent);
                details ??= gcodeDetails;
                if (details.Engine is null && gcodeDetails.Engine is not null)
                    details = details with { Engine = gcodeDetails.Engine, Version = gcodeDetails.Version };
                var extra = SlicedPlateMetadata.Read(archive, plateNumber, warnings);
                if (details.MachineModel is null && extra.MachineModelId is not null)
                    details = details with { MachineModel = extra.MachineModelId };
                var conflict = extra.NozzleConflict || (extra.Nozzle is { } a && parsed.NozzleDiameterMm is { } b && a != decimal.Round(b, 4));
                var nozzle = conflict ? null : extra.Nozzle ?? parsed.NozzleDiameterMm ?? project?.Nozzle;
                if (conflict) warnings.Add($"Plate {plateNumber}: embedded nozzle diameters conflict; nozzle diameter is unknown.");
                var minutes = extra.Minutes ?? parsed.EstimatedPrintTimeMinutes;
                var weight = extra.Weight ?? parsed.EstimatedFilamentWeightGrams;
                if (extra.Minutes is { } em && parsed.EstimatedPrintTimeMinutes is { } gm && em != gm)
                { minutes = null; warnings.Add($"Plate {plateNumber}: embedded print-time estimates conflict."); }
                if (extra.Weight is { } ew && parsed.EstimatedFilamentWeightGrams is { } gw && Math.Abs(ew - gw) > 0.02m)
                { weight = null; warnings.Add($"Plate {plateNumber}: embedded weight estimates conflict."); }
                var slots = extra.Slots.Count > 0 ? extra.Slots : parsed.FilamentSlots;
                var complete = extra.Slots.Count > 0 ? extra.MaterialsComplete : parsed.MaterialMetadataComplete && extra.MaterialsComplete;
                if (extra.Slots.Count > 0 && parsed.FilamentSlots.Count > 0 && extra.Slots.Any(e =>
                    !parsed.FilamentSlots.Any(g => string.Equals(g.MaterialType, e.MaterialType, StringComparison.OrdinalIgnoreCase))))
                { complete = false; warnings.Add($"Plate {plateNumber}: embedded filament types conflict."); }
                if (slots.Count == 0 && extra.UsedFilamentIndices is { Count: > 0 } used && project is not null)
                {
                    slots = used.Where(i => i < project.Slots.Count).Select(i => project.Slots[i]).ToArray();
                    complete = project.MaterialComplete && slots.Count == used.Count;
                }
                if (extra.Slots.Count > 0 && parsed.FilamentSlots.Count > 0 && extra.Slots.Any(e => e.DetectedColorHex is { } color
                    && parsed.FilamentSlots.Any(g => g.MaterialType == e.MaterialType && g.DetectedColorHex is not null)
                    && !parsed.FilamentSlots.Any(g => g.MaterialType == e.MaterialType && string.Equals(g.DetectedColorHex, color, StringComparison.OrdinalIgnoreCase))))
                { complete = false; warnings.Add($"Plate {plateNumber}: embedded filament colors conflict."); }
                parsed = parsed with
                {
                    NozzleDiameterMm = nozzle,
                    EstimatedPrintTimeMinutes = minutes,
                    EstimatedFilamentWeightGrams = weight,
                    FilamentSlots = slots,
                    MaterialMetadataComplete = complete
                };

                if (parsed.NozzleDiameterMm is null || (nozzleDiameterMm is not null && nozzleDiameterMm != parsed.NozzleDiameterMm))
                    nozzleComplete = false;
                nozzleDiameterMm ??= parsed.NozzleDiameterMm;
                materialComplete &= parsed.MaterialMetadataComplete;
                totalMinutes += parsed.EstimatedPrintTimeMinutes;
                totalWeightGrams += parsed.EstimatedFilamentWeightGrams;
                allSlots.AddRange(parsed.FilamentSlots);
                var usage = ReadUsage(() => JobFilamentUsageReader.Read(archive, plateNumber));
                usages.Add(usage);
                plates.Add(new FilePlate(plateNumber, names.FirstOrDefault(n => n.Number == plateNumber)?.Name, parsed.FilamentSlots,
                    parsed.EstimatedPrintTimeMinutes, parsed.EstimatedFilamentWeightGrams, Grams([usage])));

                if (parsed.Thumbnail is { } bytes)
                {
                    thumbnails.Add(new ThumbnailSpec(plateIndex, bytes));
                }
            }

            return new SlicedFileMetadata(nozzleComplete ? nozzleDiameterMm : null, totalMinutes, totalWeightGrams, plates, allSlots, thumbnails, materialComplete, details, warnings,
                Grams(usages));
        }
    }

    /// <summary>One plate's filament usage. Best effort: an unreadable usage section leaves the part with its total only, never fails the parse.</summary>
    private static JobFilamentUsageReport? ReadUsage(Func<JobFilamentUsageReport> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException) { return null; }
    }

    /// <summary>Per-filament grams of these plates together, purge included; null when any was unreadable or none says.</summary>
    private static List<FilamentGrams>? Grams(IReadOnlyList<JobFilamentUsageReport?> usages) =>
        usages.Any(u => u is null) ? null : JobFilamentUsageReader.Sum(usages!) is { Count: > 0 } grams ? grams : null;

    private static ParsedPlate ParsePlate(string gcodeContent, ZipArchiveEntry? thumbnailEntry = null)
    {
        if (string.IsNullOrWhiteSpace(gcodeContent) || gcodeContent.Contains('\0')
            || !GCodeCommandRegex().IsMatch(gcodeContent))
            throw new PartFileParsingFailedException("The file does not contain readable G-code commands.");
        var values = GCodeMetadataHelper.ParseComments(gcodeContent);
        var nozzleDiameterMm = GCodeMetadataHelper.ParseNozzleDiameter(values);
        var slots = GCodeMetadataHelper.ParseFilamentSlots(values, onlyUsed: true);

        byte[]? thumbnail = null;
        if (thumbnailEntry is not null)
        {
            using var thumbnailStream = thumbnailEntry.Open();
            using var buffer = new MemoryStream();
            thumbnailStream.CopyTo(buffer);
            thumbnail = buffer.ToArray();
        }
        else
        {
            thumbnail = GCodeMetadataHelper.ExtractEmbeddedThumbnailPng(gcodeContent);
        }

        return new ParsedPlate(
            nozzleDiameterMm is > 0 ? nozzleDiameterMm : null,
            GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(values),
            GCodeMetadataHelper.ParseFilamentWeightGrams(values),
            slots,
            thumbnail,
            slots.Count > 0 && values.TryGetValue("filament_type", out var rawTypes)
                && rawTypes.Split([',', ';'], StringSplitOptions.TrimEntries).All(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static EstimationDetails DescribeGCode(string content)
    {
        var values = GCodeMetadataHelper.ParseComments(content);
        var names = values.GetValueOrDefault("filament_settings_id")?.Split(';', StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('"')).ToArray();
        var generated = GeneratedByRegex().Match(content);
        return new("EmbeddedSlicedMetadata", values.GetValueOrDefault("printer_model"),
            values.GetValueOrDefault("printer_settings_id"), values.GetValueOrDefault("print_settings_id"),
            names, values.GetValueOrDefault("curr_bed_type"), generated.Success ? generated.Groups[1].Value : null,
            generated.Success ? generated.Groups[2].Value : null);
    }

    private readonly record struct ParsedPlate(
        decimal? NozzleDiameterMm,
        int? EstimatedPrintTimeMinutes,
        decimal? EstimatedFilamentWeightGrams,
        IReadOnlyList<DetectedFilamentSlot> FilamentSlots,
        byte[]? Thumbnail,
        bool MaterialMetadataComplete);

    [GeneratedRegex(@"generated by (OrcaSlicer|BambuStudio|SuperSlicer|PrusaSlicer)\s+([0-9][^\s;]*)", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedByRegex();

    [GeneratedRegex(@"^\s*(?:N\d+\s*)?[GMT]\d+\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GCodeCommandRegex();

    [GeneratedRegex(@"^Metadata/plate_(\d+)\.gcode$", RegexOptions.IgnoreCase)]
    private static partial Regex PlateGCodeEntryRegex();
}
