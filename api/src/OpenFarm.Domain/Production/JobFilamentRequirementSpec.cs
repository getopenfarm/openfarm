namespace OpenFarm.Domain.Production;

/// <summary>
/// One (material, color) pair a <see cref="PrintJob"/> is expected to draw from, defaulted from
/// the owning Part's own filament requirements at job creation (FR-013b). Feeds one
/// <see cref="JobFilamentConsumption"/> row per entry.
/// </summary>
public sealed record JobFilamentRequirementSpec(string? MaterialType, string? DetectedColorHex);
