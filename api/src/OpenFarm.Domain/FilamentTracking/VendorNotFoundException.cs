namespace OpenFarm.Domain.FilamentTracking;

public sealed class VendorNotFoundException(Guid vendorId)
    : Exception($"Vendor '{vendorId}' was not found.")
{
    public Guid VendorId { get; } = vendorId;
}
