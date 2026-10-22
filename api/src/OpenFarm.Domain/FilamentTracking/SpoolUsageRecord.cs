namespace OpenFarm.Domain.FilamentTracking;

public enum SpoolUsageSource
{
    Job,
    Manual,
    Correction,
}

/// <summary>
/// Append-only ledger entry for one change to a spool's remaining weight: a print's deduction, a manual usage entry, or a
/// manual weight correction. For a job entry <see cref="JobId"/> is the <c>Print</c> id; a manual entry carries whatever the
/// operator sent, unvalidated.
/// </summary>
public sealed class SpoolUsageRecord
{
    public const int MaxReasonLength = 200;

    private SpoolUsageRecord(
        Guid id,
        Guid spoolId,
        decimal gramsConsumed,
        DateTimeOffset recordedAtUtc,
        Guid? jobId,
        SpoolUsageSource source,
        decimal? remainingAfterGrams,
        string? reason)
    {
        Id = id;
        SpoolId = spoolId;
        GramsConsumed = gramsConsumed;
        RecordedAtUtc = recordedAtUtc;
        JobId = jobId;
        Source = source;
        RemainingAfterGrams = remainingAfterGrams;
        Reason = reason;
    }

    public Guid Id { get; }

    public Guid SpoolId { get; }

    /// <summary>Grams taken off the spool; negative when a correction put filament back.</summary>
    public decimal GramsConsumed { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public Guid? JobId { get; }

    public SpoolUsageSource Source { get; }

    /// <summary>The spool's remaining weight once this entry applied; null on rows written before the ledger kept it.</summary>
    public decimal? RemainingAfterGrams { get; }

    /// <summary>Why the weight was corrected; only a correction carries one.</summary>
    public string? Reason { get; }

    /// <summary>A print's deduction or a manual usage entry; <paramref name="remainingAfterGrams"/> is the spool's weight after it applied.</summary>
    public static SpoolUsageRecord Create(Guid spoolId, decimal gramsConsumed, Guid? jobId, SpoolUsageSource source, decimal remainingAfterGrams)
    {
        if (gramsConsumed <= 0)
        {
            throw new ArgumentException("Grams consumed must be positive.", nameof(gramsConsumed));
        }

        if (source == SpoolUsageSource.Correction)
        {
            throw new ArgumentException("A weight correction is recorded with Correction().", nameof(source));
        }

        return new SpoolUsageRecord(Guid.NewGuid(), spoolId, gramsConsumed, DateTimeOffset.UtcNow, jobId, source, remainingAfterGrams, null);
    }

    /// <summary>An operator moving the remaining weight from <paramref name="beforeGrams"/> to <paramref name="afterGrams"/>, in either direction.</summary>
    public static SpoolUsageRecord Correction(Guid spoolId, decimal beforeGrams, decimal afterGrams, string? reason)
    {
        if (beforeGrams == afterGrams)
        {
            throw new ArgumentException("A correction must change the weight.", nameof(afterGrams));
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > MaxReasonLength })
        {
            throw new ArgumentException($"Reason cannot exceed {MaxReasonLength} characters.", nameof(reason));
        }

        return new SpoolUsageRecord(
            Guid.NewGuid(), spoolId, beforeGrams - afterGrams, DateTimeOffset.UtcNow, null, SpoolUsageSource.Correction, afterGrams, trimmed);
    }
}
