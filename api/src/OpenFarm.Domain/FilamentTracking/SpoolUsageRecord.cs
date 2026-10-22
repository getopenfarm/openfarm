namespace OpenFarm.Domain.FilamentTracking;

public enum SpoolUsageSource
{
    Job,
    Manual,
    Correction,
    WriteOff,
    Restore,
}

/// <summary>
/// Append-only ledger entry for one change to a spool's remaining weight: a print's deduction, a manual usage entry, a
/// manual weight correction, or the write-off and restore of finishing a spool and bringing it back. For a job entry <see cref="JobId"/> is the <c>Print</c> id; a manual entry carries whatever the
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

    /// <summary>Grams taken off the spool; negative when a correction or a restore put filament back.</summary>
    public decimal GramsConsumed { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public Guid? JobId { get; }

    public SpoolUsageSource Source { get; }

    /// <summary>The spool's remaining weight once this entry applied; null on rows written before the ledger kept it.</summary>
    public decimal? RemainingAfterGrams { get; }

    /// <summary>Why the weight was corrected or written off; only a correction or a write-off carries one.</summary>
    public string? Reason { get; }

    /// <summary>A print's deduction or a manual usage entry; <paramref name="remainingAfterGrams"/> is the spool's weight after it applied.</summary>
    public static SpoolUsageRecord Create(Guid spoolId, decimal gramsConsumed, Guid? jobId, SpoolUsageSource source, decimal remainingAfterGrams)
    {
        if (gramsConsumed <= 0)
        {
            throw new ArgumentException("Grams consumed must be positive.", nameof(gramsConsumed));
        }

        if (source is not (SpoolUsageSource.Job or SpoolUsageSource.Manual))
        {
            throw new ArgumentException($"A {source} entry is recorded with {source}().", nameof(source));
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

        return new SpoolUsageRecord(
            Guid.NewGuid(), spoolId, beforeGrams - afterGrams, DateTimeOffset.UtcNow, null, SpoolUsageSource.Correction, afterGrams, CleanReason(reason));
    }

    /// <summary>Finishing a spool writes off the <paramref name="grams"/> still on it, leaving nothing.</summary>
    public static SpoolUsageRecord WriteOff(Guid spoolId, decimal grams, string? reason)
    {
        if (grams <= 0)
        {
            throw new ArgumentException("Grams written off must be positive.", nameof(grams));
        }

        return new SpoolUsageRecord(Guid.NewGuid(), spoolId, grams, DateTimeOffset.UtcNow, null, SpoolUsageSource.WriteOff, 0m, CleanReason(reason));
    }

    /// <summary>Restoring a finished spool puts back the <paramref name="grams"/> its write-off took.</summary>
    public static SpoolUsageRecord Restore(Guid spoolId, decimal grams, decimal remainingAfterGrams)
    {
        if (grams <= 0)
        {
            throw new ArgumentException("Grams restored must be positive.", nameof(grams));
        }

        return new SpoolUsageRecord(Guid.NewGuid(), spoolId, -grams, DateTimeOffset.UtcNow, null, SpoolUsageSource.Restore, remainingAfterGrams, null);
    }

    private static string? CleanReason(string? reason)
    {
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return trimmed is { Length: > MaxReasonLength }
            ? throw new ArgumentException($"Reason cannot exceed {MaxReasonLength} characters.", nameof(reason))
            : trimmed;
    }
}
