using OpenFarm.Application.Common;

namespace OpenFarm.Application.FilamentTracking;

public sealed class GetFilamentsQueryHandler(
    IFilamentRepository filamentRepository,
    IVendorRepository vendorRepository) : IQueryHandler<GetFilamentsQuery, IReadOnlyList<FilamentSummary>>
{
    public async Task<IReadOnlyList<FilamentSummary>> HandleAsync(GetFilamentsQuery query, CancellationToken cancellationToken)
    {
        var filaments = await filamentRepository.ListAsync(cancellationToken);
        var vendors = (await vendorRepository.ListAsync(cancellationToken)).ToDictionary(v => v.Id);

        return filaments
            .Select(f => new FilamentSummary(
                f.Id,
                f.VendorId,
                vendors.TryGetValue(f.VendorId, out var vendor) ? vendor.Name : string.Empty,
                f.Material,
                f.MaterialType,
                f.FilamentProfileId,
                f.Variants.Select(v => new FilamentVariantSummary(v.Id, v.ColorName, v.ColorHex)).ToList()))
            .ToList();
    }
}
