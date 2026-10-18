namespace OpenFarm.Domain.FilamentTracking;

public sealed class Filament
{
    private readonly List<FilamentVariant> _variants = [];
    private readonly List<FilamentProfileAssignment> _profileAssignments = [];

    private Filament(
        Guid id,
        Guid vendorId,
        string material,
        string materialType,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        VendorId = vendorId;
        Material = material;
        MaterialType = materialType;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid VendorId { get; private set; }

    public string Material { get; private set; }

    public string MaterialType { get; private set; }

    public IReadOnlyList<FilamentProfileAssignment> ProfileAssignments => _profileAssignments;

    public void ReplaceProfileAssignments(IEnumerable<FilamentProfileAssignment> assignments)
    {
        var rows = assignments.ToList();
        if (rows.Any(a => a.FilamentId != Id
            || a.FilamentVariantId is { } variantId && !_variants.Any(v => v.Id == variantId)))
            throw new ArgumentException("Assignments must belong to this filament and its colours.");
        if (rows.Select(a => (a.Brand, a.ModelCode, a.NozzleDiameterMm, a.FilamentVariantId)).Distinct().Count() != rows.Count)
            throw new ArgumentException("Duplicate profile assignment.");
        var retained = rows.Select(row =>
        {
            var existing = _profileAssignments.FirstOrDefault(a => a.Brand == row.Brand && a.ModelCode == row.ModelCode
                && a.NozzleDiameterMm == row.NozzleDiameterMm && a.FilamentVariantId == row.FilamentVariantId);
            if (existing is null) return row;
            existing.SetProfile(row.FilamentProfileId);
            return existing;
        }).ToList();
        _profileAssignments.Clear();
        _profileAssignments.AddRange(retained);
    }

    public Guid? ResolveProfileId(string brand, string modelCode, decimal nozzleDiameterMm, Guid variantId)
    {
        var matches = _profileAssignments.Where(a => a.Brand == brand && a.ModelCode == modelCode && a.NozzleDiameterMm == nozzleDiameterMm);
        return matches.FirstOrDefault(a => a.FilamentVariantId == variantId)?.FilamentProfileId
            ?? matches.FirstOrDefault(a => a.FilamentVariantId == null)?.FilamentProfileId;
    }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<FilamentVariant> Variants => _variants;

    public static Filament Create(Guid vendorId, string material, string materialType)
    {
        GuardDetails(material, materialType);
        return new Filament(Guid.NewGuid(), vendorId, material, materialType, DateTimeOffset.UtcNow);
    }

    public void UpdateDetails(Guid vendorId, string material, string materialType)
    {
        GuardDetails(material, materialType);
        VendorId = vendorId;
        Material = material;
        MaterialType = materialType;
    }

    private static void GuardDetails(string material, string materialType)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new ArgumentException("Material is required.", nameof(material));
        }

        if (string.IsNullOrWhiteSpace(materialType))
        {
            throw new ArgumentException("Material type is required.", nameof(materialType));
        }
    }

    public FilamentVariant AddVariant(string colorName, string colorHex)
    {
        var variant = FilamentVariant.Create(Id, colorName, colorHex);
        _variants.Add(variant);
        return variant;
    }
}
