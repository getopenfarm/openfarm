namespace OpenFarm.Domain.Production;

public sealed class FilamentConsumptionSumMismatchException(string? materialType, string? detectedColorHex)
    : Exception($"No filament requirement on this job matches (materialType: '{materialType}', detectedColorHex: '{detectedColorHex}').")
{
    public string? MaterialType { get; } = materialType;

    public string? DetectedColorHex { get; } = detectedColorHex;
}
