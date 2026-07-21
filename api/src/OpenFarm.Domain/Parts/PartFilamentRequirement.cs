namespace OpenFarm.Domain.Parts;

/// <summary>
/// Input to <see cref="Part.Create"/>/<see cref="Part.ReplaceFile"/> — a not-yet-persisted grouped
/// filament requirement, derived by <c>FilamentRequirementGrouper</c> and optionally narrowed by the
/// Confirm request's <c>filamentRequirementOverrides</c> (data-model.md, research.md §7).
/// </summary>
public sealed record FilamentRequirementSpec(
    string? MaterialType,
    string? DetectedColorHex,
    string? DetectedSubBrand,
    Guid? NarrowFilamentVariantId,
    Guid? NarrowFilamentProfileId);

/// <summary>
/// One of a <see cref="Part"/>'s one-or-more material needs — one row per distinct
/// <c>(MaterialType, DetectedColorHex)</c> pair detected across every plate/slot in the file
/// (data-model.md, research.md §7). <see cref="MaterialType"/> is <c>null</c> only for the
/// STL-only "any material" wildcard (research.md §7a).
/// </summary>
public sealed class PartFilamentRequirement
{
    private PartFilamentRequirement(
        Guid id,
        Guid organizationId,
        Guid partId,
        string? materialType,
        string? detectedColorHex,
        string? detectedSubBrand,
        Guid? narrowFilamentVariantId,
        Guid? narrowFilamentProfileId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        PartId = partId;
        MaterialType = materialType;
        DetectedColorHex = detectedColorHex;
        DetectedSubBrand = detectedSubBrand;
        NarrowFilamentVariantId = narrowFilamentVariantId;
        NarrowFilamentProfileId = narrowFilamentProfileId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid PartId { get; private set; }

    public string? MaterialType { get; private set; }

    public string? DetectedColorHex { get; private set; }

    public string? DetectedSubBrand { get; private set; }

    public Guid? NarrowFilamentVariantId { get; private set; }

    public Guid? NarrowFilamentProfileId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static PartFilamentRequirement Create(Guid organizationId, Guid partId, FilamentRequirementSpec spec) =>
        new(
            Guid.NewGuid(),
            organizationId,
            partId,
            spec.MaterialType,
            spec.DetectedColorHex,
            spec.DetectedSubBrand,
            spec.NarrowFilamentVariantId,
            spec.NarrowFilamentProfileId,
            DateTimeOffset.UtcNow);
}
