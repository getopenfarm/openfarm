using System.Text.Json.Serialization;

namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// A feed slot by where it is on the printer — its unit's <see cref="FeedUnit.Kind"/> and index, and its position in that
/// unit — never by a protocol's own numbering (Bambu tray ids, Moonraker heads), which only the printer's adapter knows.
/// </summary>
public readonly record struct FeedSlotRef(string Kind, int UnitIndex, int SlotPosition)
{
    [JsonIgnore]
    public bool IsMultiMaterialUnit => Kind == FeedUnitKindCatalog.MultiMaterialUnit;
}
