namespace OpenFarm.Domain.Production;

public sealed class ProductionSnapshotUnavailableException(Guid jobId, string? reason) : Exception(
    $"Job {jobId}: {reason ?? "Its original production inputs could not be verified; snapshot recovery is required."}");
