using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

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
        if (!printer.NozzleConfirmed || printer.ReportedNozzleDiameterMm != nozzleDiameterMm)
            throw new SlicingProfileUnconfiguredException("The printer nozzle must be confirmed by current telemetry before production.");
        var effective = await jobRequirements.ResolveAsync(job, part, cancellationToken);
        // One resolution decides everything: the spools and presets that were checked are the ones the file is made
        // with, and the stamp of exactly that configuration travels with the file.
        var resolution = await readiness.ResolveAsync(printerId, part, effective.Requirements, cancellationToken, effective.RevisionHash);
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
            return new JobFileContent(storedBuffer.ToArray(), $"{description}{storedExtension}") { ConfigurationStamp = stamp };
        }

        // A file that needs slicing never reaches the slicer without the presets that were resolved for the pin.
        var profiles = resolution.Profiles
            ?? throw new SlicingProfileUnconfiguredException("The file needs slicing, but the part has no slicing settings. Reprocess the part.");
        var result = await slicerEngine.SliceConfiguredAsync(profiles.Pin, stored, classification.Format, selections[0].MaterialType,
            nozzleDiameterMm, printerId, profiles, cancellationToken);

        var extension = result.OutputFileFormat == PartFileFormat.ThreeMf ? ".gcode.3mf" : ".gcode";
        var slicedDescription = PrintFileName.Describe(part.Name, selections.Select(s => s.MaterialType), result.EstimatedPrintTimeMinutes);
        return new JobFileContent(result.OutputFileBytes, $"{slicedDescription}{extension}") { ConfigurationStamp = stamp, Profiles = profiles };
    }
}
