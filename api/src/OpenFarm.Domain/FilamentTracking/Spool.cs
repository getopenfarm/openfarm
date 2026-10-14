namespace OpenFarm.Domain.FilamentTracking;

public sealed class Spool
{
    private Spool(
        Guid id,
        Guid organizationId,
        Guid filamentVariantId,
        string? label,
        decimal spoolWeightGrams,
        decimal remainingWeightGrams,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        FilamentVariantId = filamentVariantId;
        Label = label;
        SpoolWeightGrams = spoolWeightGrams;
        RemainingWeightGrams = remainingWeightGrams;
        HasUsageOverrun = false;
        State = SpoolState.InStorage;
        FeedSlotId = null;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid FilamentVariantId { get; }

    public string? Label { get; private set; }

    public decimal SpoolWeightGrams { get; }

    public decimal RemainingWeightGrams { get; private set; }

    public bool HasUsageOverrun { get; private set; }

    public SpoolState State { get; private set; }

    public Guid? FeedSlotId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Spool Create(Guid organizationId, Guid filamentVariantId, string? label, decimal spoolWeightGrams, decimal remainingWeightGrams)
    {
        if (spoolWeightGrams <= 0)
        {
            throw new ArgumentException("Spool weight must be positive.", nameof(spoolWeightGrams));
        }

        if (remainingWeightGrams <= 0)
        {
            throw new ArgumentException("Remaining weight must be positive.", nameof(remainingWeightGrams));
        }

        if (remainingWeightGrams > spoolWeightGrams)
        {
            throw new ArgumentException("Remaining weight cannot exceed spool weight.", nameof(remainingWeightGrams));
        }

        return new Spool(Guid.NewGuid(), organizationId, filamentVariantId, label, spoolWeightGrams, remainingWeightGrams, DateTimeOffset.UtcNow);
    }

    public void LoadOntoFeedSlot(Guid feedSlotId)
    {
        SpoolStateTransitions.EnsureValidTransition(State, SpoolState.Using);
        State = SpoolState.Using;
        FeedSlotId = feedSlotId;
    }

    /// <summary>Retains <see cref="RemainingWeightGrams"/> — FR-007's "return to storage" branch.</summary>
    public void ReturnToStorage()
    {
        SpoolStateTransitions.EnsureValidTransition(State, SpoolState.InStorage);
        State = SpoolState.InStorage;
        FeedSlotId = null;
    }

    /// <summary>Manual (FR-008) or automatic-continuation-driven (FR-009) depletion — either way clears <see cref="FeedSlotId"/>.</summary>
    public void MarkFinished()
    {
        SpoolStateTransitions.EnsureValidTransition(State, SpoolState.Finished);
        State = SpoolState.Finished;
        FeedSlotId = null;
    }

    /// <summary>Decrements <see cref="RemainingWeightGrams"/>, flooring at zero and flagging <see cref="HasUsageOverrun"/> rather than going negative (spec.md's Edge Case).</summary>
    public void RecordUsage(decimal gramsConsumed)
    {
        if (gramsConsumed <= 0)
        {
            throw new ArgumentException("Grams consumed must be positive.", nameof(gramsConsumed));
        }

        SpoolStateTransitions.EnsureUsing(State);

        var remaining = RemainingWeightGrams - gramsConsumed;
        if (remaining < 0)
        {
            RemainingWeightGrams = 0;
            HasUsageOverrun = true;
        }
        else
        {
            RemainingWeightGrams = remaining;
        }
    }

    /// <summary>FR-012 — manual override, always clears <see cref="HasUsageOverrun"/>.</summary>
    public void CorrectRemainingWeight(decimal remainingWeightGrams)
    {
        if (remainingWeightGrams < 0)
        {
            throw new ArgumentException("Remaining weight cannot be negative.", nameof(remainingWeightGrams));
        }

        if (remainingWeightGrams > SpoolWeightGrams)
        {
            throw new ArgumentException("Remaining weight cannot exceed spool weight.", nameof(remainingWeightGrams));
        }

        RemainingWeightGrams = remainingWeightGrams;
        HasUsageOverrun = false;
    }

    /// <summary>Free-text shelf label; blank clears it.</summary>
    public void Relabel(string? label) => Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
}
