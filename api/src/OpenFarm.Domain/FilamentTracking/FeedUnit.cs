namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// The physical accessory hosting one or more <see cref="FeedSlot"/>s — a Multi-Material Unit, an
/// External Spool port, or an Automatic Filament Reloader (research.md §1). <c>Kind</c> is an open
/// string resolved via <see cref="FeedUnitKindCatalog"/>, never a Postgres enum.
/// </summary>
public sealed class FeedUnit
{
    private readonly List<FeedSlot> _feedSlots = [];

    private FeedUnit(
        Guid id,
        Guid organizationId,
        Guid printerId,
        string kind,
        int unitIndex,
        string? name,
        Guid? replacesFeedUnitId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        PrinterId = printerId;
        Kind = kind;
        UnitIndex = unitIndex;
        Name = name;
        ReplacesFeedUnitId = replacesFeedUnitId;
        IsRevealed = false;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid PrinterId { get; }

    public string Kind { get; }

    public int UnitIndex { get; }

    public string? Name { get; }

    public Guid? ReplacesFeedUnitId { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid? FeedingSlotId { get; private set; }

    public string? FeedingSelectionSource { get; private set; }

    public void ConfigureReplacement(Guid? targetId)
    {
        EnsureConfigurable();
        ReplacesFeedUnitId = targetId;
    }

    public void SelectFeedingSlot(Guid? slotId, bool estimated = false)
    {
        EnsureConfigurable();
        if (slotId is not null && !_feedSlots.Any(s => s.Id == slotId && s.DeactivatedAtUtc is null))
            throw new InvalidReloaderConfigurationException("The feeding channel must be an active slot of this reloader.");
        FeedingSlotId = slotId;
        FeedingSelectionSource = slotId is null ? null : estimated ? "Estimated" : "Operator";
    }

    public void Archive()
    {
        EnsureReloader();
        ArchivedAtUtc ??= DateTimeOffset.UtcNow;
        FeedingSlotId = null;
        FeedingSelectionSource = null;
        DeactivateAllSlots();
    }

    private void EnsureReloader()
    {
        if (Kind != FeedUnitKindCatalog.AutomaticFilamentReloader)
            throw new NotAnAutomaticFilamentReloaderException(Id, Kind);
    }

    private void EnsureConfigurable()
    {
        EnsureReloader();
        if (ArchivedAtUtc is not null)
            throw new InvalidReloaderConfigurationException("This reloader has been removed.");
    }

    public bool IsRevealed { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<FeedSlot> FeedSlots => _feedSlots;

    /// <summary>Connector-derived <see cref="FeedUnitKindCatalog.MultiMaterialUnit"/>/<see cref="FeedUnitKindCatalog.ExternalSpool"/> rows — written exclusively by the telemetry sync (research.md §6).</summary>
    public static FeedUnit CreateConnectorDerived(Guid organizationId, Guid printerId, string kind, int unitIndex) =>
        new(Guid.NewGuid(), organizationId, printerId, kind, unitIndex, name: null, replacesFeedUnitId: null, DateTimeOffset.UtcNow);

    /// <summary>Manually declared by an operator (research.md §7) — the one <see cref="FeedUnitKindCatalog.AutomaticFilamentReloader"/> exception.</summary>
    public static FeedUnit CreateAutomaticFilamentReloader(Guid organizationId, Guid printerId, int unitIndex, string name, Guid? replacesFeedUnitId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An Automatic Filament Reloader requires a name.", nameof(name));
        }

        return new FeedUnit(Guid.NewGuid(), organizationId, printerId, FeedUnitKindCatalog.AutomaticFilamentReloader, unitIndex, name, replacesFeedUnitId, DateTimeOffset.UtcNow);
    }

    public FeedSlot AddFeedSlot(int slotPosition)
    {
        var slot = FeedSlot.Create(OrganizationId, Id, slotPosition);
        _feedSlots.Add(slot);
        return slot;
    }

    /// <summary>FR-024 — reveals a superseded External Spool position. Idempotent.</summary>
    public void Reveal() => IsRevealed = true;

    /// <summary>Re-hides a revealed External Spool position. Idempotent.</summary>
    public void Hide() => IsRevealed = false;

    /// <summary>Deactivates every currently-active slot — used when removing an Automatic Filament Reloader (research.md §7).</summary>
    public void DeactivateAllSlots()
    {
        foreach (var slot in _feedSlots)
        {
            slot.Deactivate();
        }
    }
}
