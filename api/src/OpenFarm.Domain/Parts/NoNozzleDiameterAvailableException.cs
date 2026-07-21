namespace OpenFarm.Domain.Parts;

/// <summary>No registered printer has a linked Machine Profile with a parseable nozzle size (FR-006a).</summary>
public sealed class NoNozzleDiameterAvailableException()
    : Exception("No registered printer has a linked Machine Profile with a known nozzle diameter.");
