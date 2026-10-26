using System.IO.Compression;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// The two FR-011 branches: an already-sliced <see cref="Part"/> (<c>RequiresSlicing == false</c>) is
/// read back from <see cref="IPartFileStorage"/> as-is; a part requiring slicing is re-sliced on
/// demand via <see cref="ISlicerEngine"/> with the presets resolved to the exact engine/version the part was
/// pinned to (<see cref="ProductionProfileReadiness"/> — no fallback to another slicer, version or preset), so a
/// job prints with the same slicer its part was confirmed with. The re-slice result is read into memory and
/// never written back to <see cref="Part.StoragePath"/> — scoped to this one dispatch only, per FR-011.
/// </summary>
public sealed class JobFileResolver(
    IPartFileStorage partFileStorage,
    ISlicerEngine slicerEngine,
    IPartFileClassifier classifier, ProductionProfileReadiness readiness,
    OpenFarm.Application.Printers.IPrinterRepository printers,
    ProductionJobRequirements jobRequirements) : IJobFileResolver
{
    public async Task<JobFileContent> ResolveAsync(Part part, PrintJob job, Guid printerId, decimal nozzleDiameterMm, CancellationToken cancellationToken)
    {
        part = job.GetProductionPart() ?? throw new ProductionSnapshotUnavailableException(job.Id, job.SnapshotRecoveryReason);
        part.EnsurePrintRequirementsComplete();
        if (printerId == Guid.Empty) throw new SlicingProfileUnconfiguredException("Assign a printer before generating a production file.");
        var printer = await printers.GetByIdAsync(printerId, cancellationToken)
            ?? throw new OpenFarm.Application.Printers.PrinterNotFoundException(printerId);
        if (!printer.ConfirmedNozzlesMm().Contains(nozzleDiameterMm))
            throw new SlicingProfileUnconfiguredException("The printer nozzle must be confirmed by current telemetry before production.");
        var effective = await jobRequirements.ResolveAsync(job, part, cancellationToken);
        // One resolution decides everything: the spools and presets that were checked are the ones the file is made
        // with, and the stamp of exactly that configuration travels with the file.
        var resolution = await readiness.ResolveAsync(printerId, part, effective.Requirements, cancellationToken, effective.RevisionHash, job);
        var stamp = await readiness.StampAsync(printerId, resolution, cancellationToken);
        var selections = resolution.Selections;
        var storagePath = effective.RevisionHash == part.RevisionHash
            ? part.StoragePath
            : await partFileStorage.GetRevisionStoragePathAsync(effective.RevisionHash, part.OriginalFileName, cancellationToken);
        await using var stored = await partFileStorage.OpenReadAsync(storagePath, cancellationToken);
        var fileName = Path.GetFileName(storagePath);
        var classification = classifier.Classify(stored, fileName);
        stored.Position = 0;
        if (!classification.RequiresSlicing)
        {
            using var storedBuffer = new MemoryStream();
            await stored.CopyToAsync(storedBuffer, cancellationToken);
            var description = PrintFileName.Describe(part.Name, selections.Select(s => s.MaterialType),
                effective.RevisionHash == part.RevisionHash ? part.EstimatedPrintTimeMinutes : null);
            var storedExtension = classification.Format == PartFileFormat.ThreeMf ? ".gcode.3mf" : ".gcode";
            var bytes = storedBuffer.ToArray();
            return new JobFileContent(bytes, $"{description}{storedExtension}", PlateToPrint(bytes)) { ConfigurationStamp = stamp };
        }

        // A file that needs slicing never reaches the slicer without the presets that were resolved for the pin.
        var profiles = resolution.Profiles
            ?? throw new SlicingProfileUnconfiguredException("The file needs slicing, but the part has no slicing settings. Reprocess the part.");
        var projectSlots = classification.Format == PartFileFormat.ThreeMf ? await ProjectSlotsAsync(stored, cancellationToken) : [];
        stored.Position = 0;
        profiles = profiles with { Slots = FilamentSlots(projectSlots, effective.Requirements) };
        var result = await slicerEngine.SliceConfiguredAsync(profiles.Pin, stored, classification.Format, selections[0].MaterialType,
            nozzleDiameterMm, printerId, profiles, cancellationToken);

        var extension = result.OutputFileFormat == PartFileFormat.ThreeMf ? ".gcode.3mf" : ".gcode";
        var slicedDescription = PrintFileName.Describe(part.Name, selections.Select(s => s.MaterialType), result.EstimatedPrintTimeMinutes);
        return new JobFileContent(result.OutputFileBytes, $"{slicedDescription}{extension}", PlateToPrint(result.OutputFileBytes))
        { ConfigurationStamp = stamp, Profiles = profiles };
    }

    /// <summary>
    /// The plate of the file to print: plate 1 when the file has it, else its only sliced plate (an exported single plate keeps
    /// its number, e.g. <c>Metadata/plate_4.gcode</c>); bare G-code is plate 1. A file holding several other plates is refused
    /// rather than one of them guessed.
    /// </summary>
    internal static int PlateToPrint(byte[] file) =>
        JobFilamentUsageReader.ChoosePlate(file, 1) ?? throw new SlicingProfileUnconfiguredException(
            $"The file holds plates {string.Join(", ", JobFilamentUsageReader.PlatesWithGCode(file))} and no plate 1. Upload a file with one plate.");

    /// <summary>
    /// Which line's filament each project slot loads. A slot's material and colour name its line — the key the part's
    /// requirements were grouped by. A blank slot is unused and takes the first filament; a slot no line claims blocks the
    /// job rather than guess. A file without project slots (STL, a geometry-only 3MF) slices with its one line's filament.
    /// </summary>
    internal static IReadOnlyList<SlotFilament> FilamentSlots(IReadOnlyList<DetectedFilamentSlot?> projectSlots, IReadOnlyList<JobFilamentRequirementSpec> lines)
    {
        if (projectSlots.Count == 0)
            return lines.Count <= 1 ? [] : throw new SlicingProfileUnconfiguredException("The file has no filament slots for its colours. Upload it as a 3MF project.");

        return projectSlots.Select((slot, index) =>
        {
            if (slot is null) return new SlotFilament(0, null);
            for (var line = 0; line < lines.Count; line++)
            {
                if (string.Equals(lines[line].MaterialType, slot.MaterialType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(lines[line].DetectedColorHex, slot.DetectedColorHex, StringComparison.OrdinalIgnoreCase))
                    return new SlotFilament(line, slot.DetectedColorHex);
            }

            throw new SlicingProfileUnconfiguredException(
                $"Filament {index + 1} of the file ({slot.MaterialType} {slot.DetectedColorHex}) matches no job line. Reprocess the part.");
        }).ToList();
    }

    private static async Task<IReadOnlyList<DetectedFilamentSlot?>> ProjectSlotsAsync(Stream stored, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipArchive(stored, ZipArchiveMode.Read, leaveOpen: true);
            if (SlicedPlateMetadata.Find(archive, "Metadata/project_settings.config") is not { } settings) return [];
            using var reader = new StreamReader(settings.Open());
            return ProjectSettingsMetadata.ReadAllSlots(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }
}
