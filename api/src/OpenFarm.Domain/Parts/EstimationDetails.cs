namespace OpenFarm.Domain.Parts;

/// <summary>Describes the settings behind a library estimate, independently of production settings.</summary>
public sealed record EstimationDetails(
    string Source,
    string? MachineModel = null,
    string? MachineProfileName = null,
    string? ProcessProfileName = null,
    IReadOnlyList<string>? FilamentProfileNames = null,
    string? BuildPlate = null,
    string? Engine = null,
    string? Version = null);
