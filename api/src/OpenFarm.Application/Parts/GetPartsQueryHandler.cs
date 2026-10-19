using OpenFarm.Application.Common;

namespace OpenFarm.Application.Parts;

public sealed class GetPartsQueryHandler(IPartRepository partRepository) : IQueryHandler<GetPartsQuery, PartsPage>
{
    public async Task<PartsPage> HandleAsync(GetPartsQuery query, CancellationToken cancellationToken)
    {
        var (parts, totalCount) = await partRepository.ListAsync(query.Tags, query.Page, query.PageSize, cancellationToken, query.Sorting);

        var summaries = parts.Select(part =>
        {
            // "Any material" (STL wildcard, research.md §7a) has materialTypes: [] — never [null] —
            // per contracts/part-library-api.md.
            var materialTypes = part.FilamentRequirements
                .Select(r => r.MaterialType)
                .Where(m => m is not null)
                .Select(m => m!)
                .Distinct()
                .ToList();

            var catalogThumbnail = part.Thumbnails.OrderBy(t => t.PlateIndex).FirstOrDefault();

            return new PartSummary(
                part.Id,
                part.Name,
                part.FileFormat.ToString(),
                part.RequiresSlicing,
                part.NozzleDiameterMm,
                part.EstimateStatus.ToString(),
                part.EstimatedPrintTimeMinutes,
                part.EstimatedFilamentWeightGrams,
                catalogThumbnail is null ? null : Convert.ToBase64String(catalogThumbnail.Bytes),
                materialTypes,
                part.Tags.Select(t => t.Name).ToList(),
                part.PrintRequirementsStatus.ToString(), part.ProcessingWarnings, part.EstimateUnavailableReason, part.Estimation, part.UnitsPerPrint,
                part.FilamentRequirements.Select(r => new PartColorGrams(r.MaterialType, r.DetectedColorHex, r.EstimatedGrams, r.NarrowFilamentVariantId)).ToList());
        }).ToList();

        return new PartsPage(summaries, totalCount);
    }
}
