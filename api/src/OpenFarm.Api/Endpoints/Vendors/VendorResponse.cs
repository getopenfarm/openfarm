using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Vendors;

public sealed class VendorResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public static VendorResponse FromDomain(Vendor vendor) => new() { Id = vendor.Id, Name = vendor.Name };

    public static VendorResponse FromSummary(VendorSummary summary) => new() { Id = summary.Id, Name = summary.Name };
}
