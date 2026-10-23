using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class FilamentVariantResponse
{
    public decimal? PricePerKg { get; set; }

    /// <summary>This colour's own low-stock threshold; null when it uses the filament's default.</summary>
    public decimal? LowStockGrams { get; set; }

    /// <summary>This colour's own threshold in days left; null when it uses the filament's default.</summary>
    public int? LowStockDays { get; set; }

    public Guid Id { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public static FilamentVariantResponse FromDomain(FilamentVariant variant) =>
        new()
        {
            Id = variant.Id,
            ColorName = variant.ColorName,
            ColorHex = variant.ColorHex,
            PricePerKg = variant.PricePerKg,
            LowStockGrams = variant.LowStockGrams,
            LowStockDays = variant.LowStockDays,
        };

    public static FilamentVariantResponse FromSummary(FilamentVariantSummary summary) =>
        new()
        {
            Id = summary.Id,
            ColorName = summary.ColorName,
            ColorHex = summary.ColorHex,
            PricePerKg = summary.PricePerKg,
            LowStockGrams = summary.LowStockGrams,
            LowStockDays = summary.LowStockDays,
        };
}

public sealed class FilamentResponse
{
    public decimal? PricePerKg { get; set; }

    /// <summary>Default low-stock threshold for this filament's colours that have been stocked; null for no alert.</summary>
    public decimal? LowStockGrams { get; set; }

    /// <summary>Default threshold in days left at the last 30 days' pace; null for no alert by days.</summary>
    public int? LowStockDays { get; set; }

    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public string VendorName { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    public string MaterialType { get; set; } = string.Empty;

    public IReadOnlyList<FilamentProfileAssignmentInput> ProfileAssignments { get; set; } = [];

    public IReadOnlyList<FilamentVariantResponse> Variants { get; set; } = [];

    public static FilamentResponse FromDomain(Filament filament, string vendorName) => new()
    {
        Id = filament.Id,
        PricePerKg = filament.PricePerKg,
        LowStockGrams = filament.LowStockGrams,
        LowStockDays = filament.LowStockDays,
        VendorId = filament.VendorId,
        VendorName = vendorName,
        Material = filament.Material,
        MaterialType = filament.MaterialType,
        ProfileAssignments = filament.ProfileAssignments.Select(a => new FilamentProfileAssignmentInput(a.Brand, a.ModelCode, a.NozzleDiameterMm, a.FilamentProfileId, filament.Variants.FirstOrDefault(v => v.Id == a.FilamentVariantId)?.ColorHex)).ToList(),
        Variants = filament.Variants.Select(FilamentVariantResponse.FromDomain).ToList(),
    };

    public static FilamentResponse FromSummary(FilamentSummary summary) => new()
    {
        Id = summary.Id,
        PricePerKg = summary.PricePerKg,
        LowStockGrams = summary.LowStockGrams,
        LowStockDays = summary.LowStockDays,
        VendorId = summary.VendorId,
        VendorName = summary.VendorName,
        Material = summary.Material,
        MaterialType = summary.MaterialType,
        ProfileAssignments = summary.ProfileAssignments,
        Variants = summary.Variants.Select(FilamentVariantResponse.FromSummary).ToList(),
    };
}
