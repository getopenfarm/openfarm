namespace OpenFarm.Application.FilamentTracking;

public sealed record CorrectRemainingWeightCommand(Guid SpoolId, decimal RemainingWeightGrams);
