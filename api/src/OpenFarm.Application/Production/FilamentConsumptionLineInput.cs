namespace OpenFarm.Application.Production;

/// <summary>FR-013/FR-013a's operator-supplied breakdown — shared shape for /complete and /fail.</summary>
public sealed record FilamentConsumptionLineInput(string? MaterialType, string? DetectedColorHex, Guid? SpoolId, decimal GramsConsumed);
