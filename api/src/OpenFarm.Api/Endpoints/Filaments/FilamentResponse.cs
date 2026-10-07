using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class FilamentVariantResponse
{
    public Guid Id { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public static FilamentVariantResponse FromDomain(FilamentVariant variant) =>
        new() { Id = variant.Id, ColorName = variant.ColorName, ColorHex = variant.ColorHex };

    public static FilamentVariantResponse FromSummary(FilamentVariantSummary summary) =>
        new() { Id = summary.Id, ColorName = summary.ColorName, ColorHex = summary.ColorHex };
}

public sealed class FilamentResponse
{
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
        VendorId = summary.VendorId,
        VendorName = summary.VendorName,
        Material = summary.Material,
        MaterialType = summary.MaterialType,
        ProfileAssignments = summary.ProfileAssignments,
        Variants = summary.Variants.Select(FilamentVariantResponse.FromSummary).ToList(),
    };
}
