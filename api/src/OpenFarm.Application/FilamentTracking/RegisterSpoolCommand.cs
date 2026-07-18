namespace OpenFarm.Application.FilamentTracking;

public sealed record RegisterSpoolCommand(Guid FilamentVariantId, string? Label, decimal SpoolWeightGrams, decimal? RemainingWeightGrams);
