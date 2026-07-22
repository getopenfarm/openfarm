namespace OpenFarm.Application.Profiles;

/// <summary>Per-kind row counts for one ingestion run (contracts/system-profile-ingestion.md) — logged, never persisted.</summary>
public sealed record ProfileKindIngestionCounts(int Created, int Updated, int MarkedStale);

public sealed record IngestionSummary(
    int VendorsScanned,
    int VendorsSkipped,
    ProfileKindIngestionCounts Machine,
    ProfileKindIngestionCounts Process,
    ProfileKindIngestionCounts Filament);

/// <summary>
/// Walks a bundled slicer leaf's vendor profile index files and upserts <c>Origin=System</c> rows for
/// machine, process, and filament profiles (research.md §0/§1, contracts/system-profile-ingestion.md).
/// Re-runnable any time — idempotent against unchanged vendor data, non-destructive against a genuinely
/// removed vendor row (flagged stale, never deleted).
/// </summary>
public interface ISystemProfileIngestionService
{
    Task<IngestionSummary> IngestAsync(string bundleLeafPath, CancellationToken cancellationToken);
}
