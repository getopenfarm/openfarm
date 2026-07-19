namespace OpenFarm.Domain.FilamentTracking;

public sealed class Filament
{
    private readonly List<FilamentVariant> _variants = [];

    private Filament(
        Guid id,
        Guid organizationId,
        Guid vendorId,
        string material,
        string materialType,
        Guid? filamentProfileId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        VendorId = vendorId;
        Material = material;
        MaterialType = materialType;
        FilamentProfileId = filamentProfileId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid VendorId { get; }

    public string Material { get; }

    public string MaterialType { get; }

    public Guid? FilamentProfileId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<FilamentVariant> Variants => _variants;

    public static Filament Create(Guid organizationId, Guid vendorId, string material, string materialType, Guid? filamentProfileId)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new ArgumentException("Material is required.", nameof(material));
        }

        if (string.IsNullOrWhiteSpace(materialType))
        {
            throw new ArgumentException("Material type is required.", nameof(materialType));
        }

        return new Filament(Guid.NewGuid(), organizationId, vendorId, material, materialType, filamentProfileId, DateTimeOffset.UtcNow);
    }

    public FilamentVariant AddVariant(string colorName, string colorHex)
    {
        var variant = FilamentVariant.Create(Id, colorName, colorHex);
        _variants.Add(variant);
        return variant;
    }
}
