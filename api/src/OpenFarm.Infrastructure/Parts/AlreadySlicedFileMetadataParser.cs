using System.IO.Compression;
using System.Text.RegularExpressions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Fixture-replay, test-after (research.md §12, mirrors ProfileBundleParser/BambuMqttMessageParser's
// existing carve-out) — reads a standalone G-code's header/footer comments, or walks every plate's
// own embedded G-code inside a .gcode.3mf (Metadata/plate_<n>.gcode + Metadata/plate_<n>.png),
// summing estimates and grouping filament requirements across every plate/slot (research.md §7).
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

        var plate = ParsePlate(content, plateIndex: 0);
        if (plate is null)
        {
            throw new PartFileParsingFailedException("Could not parse nozzle diameter and filament type from the G-code's header comments.");
        }

        return new SlicedFileMetadata(
            plate.Value.NozzleDiameterMm,
            plate.Value.EstimatedPrintTimeMinutes ?? 0,
            plate.Value.EstimatedFilamentWeightGrams ?? 0,
            PlateCount: 1,
            plate.Value.FilamentSlots,
            plate.Value.Thumbnail is null ? [] : [new ThumbnailSpec(0, plate.Value.Thumbnail)]);
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
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (plateNumbers.Count == 0)
            {
                throw new PartFileParsingFailedException("No per-plate G-code was found inside the 3MF's Metadata folder.");
            }

            decimal? nozzleDiameterMm = null;
            var totalMinutes = 0;
            var totalWeightGrams = 0m;
            var allSlots = new List<DetectedFilamentSlot>();
            var thumbnails = new List<ThumbnailSpec>();

            foreach (var plateNumber in plateNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var gcodeEntry = archive.GetEntry($"Metadata/plate_{plateNumber}.gcode");
                if (gcodeEntry is null)
                {
                    continue;
                }

                string gcodeContent;
                using (var entryReader = new StreamReader(gcodeEntry.Open()))
                {
                    gcodeContent = await entryReader.ReadToEndAsync(cancellationToken);
                }

                var plateIndex = plateNumber - 1;
                var thumbnailEntry = archive.GetEntry($"Metadata/plate_{plateNumber}.png");
                var parsed = ParsePlate(gcodeContent, plateIndex, thumbnailEntry, archive);

                if (parsed is null)
                {
                    continue;
                }

                nozzleDiameterMm ??= parsed.Value.NozzleDiameterMm;
                totalMinutes += parsed.Value.EstimatedPrintTimeMinutes ?? 0;
                totalWeightGrams += parsed.Value.EstimatedFilamentWeightGrams ?? 0;
                allSlots.AddRange(parsed.Value.FilamentSlots);

                if (parsed.Value.Thumbnail is { } bytes)
                {
                    thumbnails.Add(new ThumbnailSpec(plateIndex, bytes));
                }
            }

            if (nozzleDiameterMm is null || allSlots.Count == 0)
            {
                throw new PartFileParsingFailedException("Could not parse nozzle diameter and filament type from the 3MF's embedded plate G-code.");
            }

            return new SlicedFileMetadata(nozzleDiameterMm.Value, totalMinutes, totalWeightGrams, plateNumbers.Count, allSlots, thumbnails);
        }
    }

    private static ParsedPlate? ParsePlate(string gcodeContent, int plateIndex, ZipArchiveEntry? thumbnailEntry = null, ZipArchive? archive = null)
    {
        var values = GCodeMetadataHelper.ParseComments(gcodeContent);
        var nozzleDiameterMm = GCodeMetadataHelper.ParseNozzleDiameter(values);
        var slots = GCodeMetadataHelper.ParseFilamentSlots(values);

        if (nozzleDiameterMm is null || slots.Count == 0)
        {
            return null;
        }

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
            nozzleDiameterMm.Value,
            GCodeMetadataHelper.ParseEstimatedPrintTimeMinutes(values),
            GCodeMetadataHelper.ParseFilamentWeightGrams(values),
            slots,
            thumbnail);
    }

    private readonly record struct ParsedPlate(
        decimal NozzleDiameterMm,
        int? EstimatedPrintTimeMinutes,
        decimal? EstimatedFilamentWeightGrams,
        IReadOnlyList<DetectedFilamentSlot> FilamentSlots,
        byte[]? Thumbnail);

    [GeneratedRegex(@"^Metadata/plate_(\d+)\.gcode$", RegexOptions.IgnoreCase)]
    private static partial Regex PlateGCodeEntryRegex();
}
