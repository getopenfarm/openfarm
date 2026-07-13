namespace OpenFarm.Domain.Printers;

public sealed record AmsTray(
    int SlotIndex,
    bool Empty,
    string? MaterialType,
    string? ColorHex,
    string? SubBrand,
    int? RemainPercent,
    bool Active);

public sealed record AmsUnit(int UnitIndex, IReadOnlyList<AmsTray> Trays);
