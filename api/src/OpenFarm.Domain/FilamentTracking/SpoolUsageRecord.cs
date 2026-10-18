namespace OpenFarm.Domain.FilamentTracking;

public enum SpoolUsageSource
{
    Job,
    Manual,
}

/// <summary>Append-only usage ledger entry (data-model.md) — <see cref="JobId"/> is reserved-unused until Phase 8's Job entity exists.</summary>
public sealed class SpoolUsageRecord
{
    private SpoolUsageRecord(
        Guid id,
        Guid spoolId,
        decimal gramsConsumed,
        DateTimeOffset recordedAtUtc,
        Guid? jobId,
        SpoolUsageSource source)
    {
        Id = id;
        SpoolId = spoolId;
        GramsConsumed = gramsConsumed;
        RecordedAtUtc = recordedAtUtc;
        JobId = jobId;
        Source = source;
    }

    public Guid Id { get; }

    public Guid SpoolId { get; }

    public decimal GramsConsumed { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public Guid? JobId { get; }

    public SpoolUsageSource Source { get; }

    public static SpoolUsageRecord Create(Guid spoolId, decimal gramsConsumed, Guid? jobId, SpoolUsageSource source)
    {
        if (gramsConsumed <= 0)
        {
            throw new ArgumentException("Grams consumed must be positive.", nameof(gramsConsumed));
        }

        return new SpoolUsageRecord(Guid.NewGuid(), spoolId, gramsConsumed, DateTimeOffset.UtcNow, jobId, source);
    }
}
