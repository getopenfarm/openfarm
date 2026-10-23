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

    public decimal? PricePerKg { get; private set; }

    public void SetPricePerKg(decimal? price)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price must be non-negative.");
        PricePerKg = price;
    }

    /// <summary>Grams on hand below which this filament's colours show as low, unless a colour sets its own; null for no alert.</summary>
    public decimal? LowStockGrams { get; private set; }

    public void SetLowStockGrams(decimal? grams)
    {
        if (grams < 0) throw new ArgumentOutOfRangeException(nameof(grams), "Low stock threshold must be non-negative.");
        LowStockGrams = grams;
    }

    /// <summary>The highest days threshold, on a filament or a colour.</summary>
    public const int MaxLowStockDays = 365;

    /// <summary>Days left below which this filament's colours show as low, unless a colour sets its own; null for no alert by days.</summary>
    public int? LowStockDays { get; private set; }

    public void SetLowStockDays(int? days)
    {
        if (days is < 0 or > MaxLowStockDays)
            throw new ArgumentOutOfRangeException(nameof(days), $"Low stock days must be 0 to {MaxLowStockDays}.");
        LowStockDays = days;
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
