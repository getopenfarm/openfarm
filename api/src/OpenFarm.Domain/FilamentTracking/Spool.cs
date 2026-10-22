namespace OpenFarm.Domain.FilamentTracking;

public sealed class Spool
{
    /// <summary>Longest supplier or order number kept.</summary>
    public const int MaxPurchaseTextLength = 100;

    public const int MaxRfidIdLength = 64;

    private Spool(
        Guid id,
        Guid filamentVariantId,
        string? label,
        decimal spoolWeightGrams,
        decimal remainingWeightGrams,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
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

    public Guid FilamentVariantId { get; }

    public string? Label { get; private set; }

    public decimal SpoolWeightGrams { get; }

    public decimal RemainingWeightGrams { get; private set; }

    public bool HasUsageOverrun { get; private set; }

    public SpoolState State { get; private set; }

    public Guid? FeedSlotId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>What was paid for the whole spool; null when unknown. Zero is a real price (a free spool), not unknown.</summary>
    public decimal? Price { get; private set; }

    public string? Supplier { get; private set; }

    public string? OrderNumber { get; private set; }

    /// <summary>The day the spool arrived: a calendar date, with no time or zone.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>The id its RFID tag carries, so a printer that reads the tag loads it; null when untagged. A finished spool keeps it for history.</summary>
    public string? RfidId { get; private set; }

    /// <summary><see cref="Price"/> over the spool's full weight in kilograms — what was paid covers the whole spool, not what is left of it.</summary>
    public decimal? PricePerKg => PricePerKgOf(Price, SpoolWeightGrams);

    /// <summary>The price paid for a spool of <paramref name="spoolWeightGrams"/> as a price per kilogram; null when the price is unknown.</summary>
    public static decimal? PricePerKgOf(decimal? price, decimal spoolWeightGrams) =>
        price is { } paid ? paid / (spoolWeightGrams / 1000m) : null;

    public static Spool Create(Guid filamentVariantId, string? label, decimal spoolWeightGrams, decimal remainingWeightGrams)
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

        return new Spool(Guid.NewGuid(), filamentVariantId, label, spoolWeightGrams, remainingWeightGrams, DateTimeOffset.UtcNow);
    }

    public void LoadOntoFeedSlot(Guid feedSlotId)
    {
        SpoolStateTransitions.EnsureValidTransition(State, SpoolState.Using);
        State = SpoolState.Using;
        FeedSlotId = feedSlotId;
    }

    /// <summary>Retains <see cref="RemainingWeightGrams"/> — FR-007's "return to storage" branch. Only a loaded spool returns; a finished one comes back through <see cref="Restore"/>.</summary>
    public void ReturnToStorage()
    {
        if (State != SpoolState.Using)
        {
            throw new InvalidSpoolStateTransitionException(State, SpoolState.InStorage);
        }

        State = SpoolState.InStorage;
        FeedSlotId = null;
    }

    /// <summary>
    /// Manual (FR-008), from storage or a slot, or automatic-continuation-driven (FR-009) depletion — either way clears
    /// <see cref="FeedSlotId"/>. Whatever is left is written off, so the returned history entry is null only when nothing was.
    /// </summary>
    public SpoolUsageRecord? MarkFinished(string? reason = null)
    {
        SpoolStateTransitions.EnsureValidTransition(State, SpoolState.Finished);
        var writeOff = RemainingWeightGrams > 0 ? SpoolUsageRecord.WriteOff(Id, RemainingWeightGrams, reason) : null;
        State = SpoolState.Finished;
        FeedSlotId = null;
        RemainingWeightGrams = 0;
        return writeOff;
    }

    /// <summary>
    /// Back to storage, never into a slot. The grams come back only when <paramref name="latest"/>, the spool's newest history
    /// entry, is the write-off that finished it: a weight corrected since then is kept as it is.
    /// </summary>
    public SpoolUsageRecord? Restore(SpoolUsageRecord? latest)
    {
        if (State != SpoolState.Finished)
        {
            throw new InvalidSpoolStateTransitionException(State, SpoolState.InStorage);
        }

        State = SpoolState.InStorage;
        if (latest is not { Source: SpoolUsageSource.WriteOff } || latest.SpoolId != Id)
        {
            return null;
        }

        RemainingWeightGrams += latest.GramsConsumed;
        return SpoolUsageRecord.Restore(Id, latest.GramsConsumed, RemainingWeightGrams);
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

    /// <summary>Ties the spool to the tag a printer reads. The same id again does nothing; a spool keeps the first tag it got.</summary>
    public void AttachRfid(string rfidId)
    {
        var id = rfidId.Trim();
        if (id.Length == 0 || id.Length > MaxRfidIdLength)
        {
            throw new ArgumentException($"An RFID id is 1 to {MaxRfidIdLength} characters.", nameof(rfidId));
        }

        if (RfidId == id)
        {
            return;
        }

        RfidId = RfidId is null ? id : throw new SpoolRfidConflictException("The spool already has another RFID tag.");
    }

    /// <summary>Free-text shelf label; blank clears it.</summary>
    public void Relabel(string? label) => Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

    /// <summary>Replaces the purchase info; blank text is cleared. When any value is invalid nothing changes.</summary>
    public void SetPurchase(decimal? price, string? supplier, string? orderNumber, DateOnly? receivedOn)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        }

        var cleanSupplier = CleanPurchaseText(supplier, nameof(supplier));
        var cleanOrderNumber = CleanPurchaseText(orderNumber, nameof(orderNumber));

        Price = price;
        Supplier = cleanSupplier;
        OrderNumber = cleanOrderNumber;
        ReceivedOn = receivedOn;
    }

    private static string? CleanPurchaseText(string? text, string parameterName)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return trimmed is { Length: > MaxPurchaseTextLength }
            ? throw new ArgumentException($"Cannot exceed {MaxPurchaseTextLength} characters.", parameterName)
            : trimmed;
    }
}
