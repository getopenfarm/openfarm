using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Parts;

public sealed class GetPartQueryHandler(
    IPartRepository partRepository,
    IFilamentRepository filamentRepository,
    IFilamentProfileRepository filamentProfileRepository, IPartFileStorage? fileStorage = null) : IQueryHandler<GetPartQuery, PartDetail?>
{
    public async Task<PartDetail?> HandleAsync(GetPartQuery query, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(query.PartId, cancellationToken);
        if (part is null)
        {
            return null;
        }

        var requirements = new List<PartFilamentRequirementDetail>();
        foreach (var r in part.FilamentRequirements)
        {
            var variantUnresolved = r.NarrowFilamentVariantId is { } variantId
                && await filamentRepository.FindVariantByIdAsync(variantId, cancellationToken) is null;
            var profileUnresolved = r.NarrowFilamentProfileId is { } profileId
                && await filamentProfileRepository.GetByIdAsync(profileId, cancellationToken) is null;

            requirements.Add(new PartFilamentRequirementDetail(
                r.Id, r.MaterialType, r.DetectedColorHex, r.DetectedSubBrand,
                r.NarrowFilamentVariantId, r.NarrowFilamentProfileId, variantUnresolved, profileUnresolved, r.EstimatedGrams));
        }

        var slicer = part.SlicerEngineOverride is { } engine && part.SlicerVersionUsed is { } version
            ? new SlicerSelectionView(engine.ToString(), version)
            : null;

        PartObjectInventory? inventory = null;
        if (fileStorage is not null)
        {
            try { inventory = await PartObjectInventory.ReadAsync(fileStorage, part.StoragePath, part.PlateNumber, cancellationToken); }
            catch (IOException) { /* Missing historical bytes must not make catalog details inaccessible. */ }
        }

        return new PartDetail(
            part.Id,
            part.Name,
            part.FileFormat.ToString(),
            part.RequiresSlicing,
            part.OriginalFileName,
            part.FileSizeBytes,
            part.NozzleDiameterMm,
            part.EstimateStatus.ToString(),
            part.EstimatedPrintTimeMinutes,
            part.EstimatedFilamentWeightGrams,
            slicer,
            requirements,
            part.PlateNumber,
            part.Thumbnails.OrderBy(t => t.PlateIndex).Select(t => new ThumbnailView(t.PlateIndex, Convert.ToBase64String(t.Bytes))).ToList(),
            part.Tags.Select(t => t.Name).ToList(), part.UnitsPerPrint,
            part.PrintRequirementsStatus.ToString(), part.ProcessingWarnings, part.EstimateUnavailableReason, part.Estimation, part.PlateName, part.FolderId,
            inventory?.Count, inventory?.Source, inventory?.Count is { } count && count != part.UnitsPerPrint);
    }
}
