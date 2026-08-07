namespace OpenFarm.Application.Production;

public sealed record SetRunLineBackfillLimitsCommand(Guid RunLineId, int BackfillPrinterCap, int? BackfillCumulativeUnitCeiling);
