namespace OpenFarm.Application.Printers;

/// <summary>Where one of a file's filaments is fed from, in Bambu's <c>ams_mapping2</c> terms: an AMS unit and slot, or the external spool.</summary>
public readonly record struct FeedTarget(int AmsId, int SlotId);

/// <summary>
/// Tells the printer which feed each filament of the project file uses, indexed by project filament (the G-code's <c>T&lt;n&gt;</c>).
/// <see cref="Trays"/> is the legacy <c>ams_mapping</c> (absolute tray id, -1 for unused or external); <see cref="Targets"/> is <c>ams_mapping2</c>.
/// <see cref="UseAms"/> is false when every filament comes from the external spool.
/// </summary>
public sealed record FilamentFeedMapping(bool UseAms, IReadOnlyList<int> Trays, IReadOnlyList<FeedTarget> Targets);
