namespace OpenFarm.Domain.FilamentTracking;

public sealed class DuplicateVendorNameException(string name)
    : Exception($"A vendor named '{name}' already exists.")
{
    public string VendorName { get; } = name;
}
