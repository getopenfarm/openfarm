using OpenFarm.Application.Common;

namespace OpenFarm.Application.FilamentTracking;

public sealed class GetVendorsQueryHandler(IVendorRepository vendorRepository) : IQueryHandler<GetVendorsQuery, IReadOnlyList<VendorSummary>>
{
    public async Task<IReadOnlyList<VendorSummary>> HandleAsync(GetVendorsQuery query, CancellationToken cancellationToken)
    {
        var vendors = await vendorRepository.ListAsync(cancellationToken);
        return vendors.Select(v => new VendorSummary(v.Id, v.Name)).ToList();
    }
}
