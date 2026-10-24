using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

/// <summary>What a printer reports for one feed unit right now, labelled the way the printer's model names it.</summary>
public sealed class FeedReadingResponse
{
    /// <summary><c>MultiMaterialUnit</c> or <c>ExternalSpool</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public int UnitIndex { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public IReadOnlyList<FeedSlotReadingResponse> Slots { get; set; } = [];

    public static IReadOnlyList<FeedReadingResponse> From(IReadOnlyList<FeedUnitReading> readings, PrinterModel? model) =>
        readings.Select(unit => new FeedReadingResponse
        {
            Kind = unit.Kind,
            UnitIndex = unit.UnitIndex,
            DisplayLabel = FeedUnitDisplay.UnitLabel(unit.Kind, unit.UnitIndex, name: null, model),
            Slots = unit.Slots.Select(slot => new FeedSlotReadingResponse
            {
                SlotPosition = slot.SlotPosition,
                ExtruderIndex = slot.ExtruderIndex,
                DisplayLabel = FeedUnitDisplay.SlotLabel(unit.Kind, unit.UnitIndex, name: null, slot.SlotPosition, model),
                Empty = slot.Empty,
                MaterialType = slot.MaterialType,
                ColorHex = slot.ColorHex,
                SubBrand = slot.SubBrand,
                RemainPercent = slot.RemainPercent,
                Active = slot.Active,
                RfidId = slot.RfidId,
            }).ToList(),
        }).ToList();
}

/// <summary>What a printer reports for one feed slot right now.</summary>
public sealed class FeedSlotReadingResponse
{
    public int SlotPosition { get; set; }

    /// <summary>The extruder this slot feeds, an index into the printer model's extruders.</summary>
    public int ExtruderIndex { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public bool Empty { get; set; }

    public string? MaterialType { get; set; }

    public string? ColorHex { get; set; }

    public string? SubBrand { get; set; }

    public int? RemainPercent { get; set; }

    /// <summary>The printer reports this slot as the one feeding its extruder now.</summary>
    public bool Active { get; set; }

    /// <summary>The spool's RFID tag id; null without one.</summary>
    public string? RfidId { get; set; }
}
