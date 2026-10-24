using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Tells the printer which feed slot each filament of the project file is loaded from, indexed by project filament (the
/// G-code's <c>T&lt;n&gt;</c>); null for a filament no job line uses. Each adapter turns it into its own protocol's terms
/// (Bambu's <c>ams_mapping</c>, the U1's <c>map_table</c>).
/// </summary>
public sealed record FilamentFeedMapping(IReadOnlyList<FeedSlotRef?> Tools);
