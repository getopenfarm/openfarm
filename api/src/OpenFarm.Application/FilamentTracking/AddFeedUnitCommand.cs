namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Kind-discriminated (research.md §13): <c>AutomaticFilamentReloader</c> requires <see cref="Name"/> +
/// <see cref="SlotCount"/> (+ optional <see cref="ReplacesFeedUnitId"/>) and creates new rows;
/// <c>ExternalSpool</c> needs no extra fields — finds the printer's own row and reveals it.
/// </summary>
public sealed record AddFeedUnitCommand(Guid PrinterId, string Kind, string? Name, int? SlotCount, Guid? ReplacesFeedUnitId);
