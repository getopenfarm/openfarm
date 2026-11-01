using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Production;

/// <summary>Immutable catalog inputs; printer profiles and inventory remain live dispatch inputs.</summary>
public sealed record ProductionPartSnapshot(
    Guid PartId, string Name, PartFileFormat FileFormat, bool RequiresSlicing,
    string OriginalFileName, string StoragePath, long FileSizeBytes, decimal? NozzleDiameterMm,
    bool MaterialMetadataComplete, SlicerEngine? SlicerEngine, string? SlicerVersion,
    EstimateStatus EstimateStatus, int? EstimatedPrintTimeMinutes, decimal? EstimatedFilamentWeightGrams,
    int? PlateNumber, int UnitsPerPrint, string RevisionHash, int RevisionNumber,
    IReadOnlyList<FilamentRequirementSpec> FilamentRequirements, string? ObjectManifestJson = null)
{
    public static ProductionPartSnapshot Capture(Part part, SlicerRef? slicer = null) => new(
        part.Id, part.Name, part.FileFormat, part.RequiresSlicing,
        part.OriginalFileName, part.StoragePath, part.FileSizeBytes, part.NozzleDiameterMm,
        part.MaterialMetadataComplete, slicer?.Engine ?? part.SlicerEngineOverride,
        slicer?.Version ?? part.SlicerVersionUsed, part.EstimateStatus, part.EstimatedPrintTimeMinutes,
        part.EstimatedFilamentWeightGrams, part.PlateNumber, part.UnitsPerPrint, part.RevisionHash,
        part.RevisionNumber, part.FilamentRequirements.Select(r => new FilamentRequirementSpec(
            r.MaterialType, r.DetectedColorHex, r.DetectedSubBrand, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId, r.EstimatedGrams)).ToArray(), part.ObjectManifestJson);
}
