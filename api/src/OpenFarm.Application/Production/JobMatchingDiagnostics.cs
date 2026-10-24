namespace OpenFarm.Application.Production;

public sealed record MatchingCheck(string Code, string Label, string Status, string? Required, string? Actual,
    string? Message = null, string? ConfigurationUrl = null);
public sealed record MatchingFilamentRequirement(string? MaterialType, string? DetectedColorHex,
    Guid? SelectedVariantId, string? SelectedVariantName, string? SelectedColorHex, Guid? ProfileConstraintId);
public sealed record PrinterMatchingDiagnostics(Guid PrinterId, string PrinterName, bool Eligible,
    IReadOnlyList<MatchingCheck> Checks, string? SchedulingNote);
public sealed record JobMatchingDiagnostics(Guid JobId, DateTimeOffset EvaluatedAtUtc, string RequirementSource,
    string Outcome, string Summary, IReadOnlyList<MatchingFilamentRequirement> Requirements,
    IReadOnlyList<MatchingCheck> JobChecks, IReadOnlyList<PrinterMatchingDiagnostics> Printers);
/// <param name="ExtruderIndex">The extruder the spool's slot feeds.</param>
/// <param name="NozzleMm">That extruder's nozzle confirmed on the current connection; null when unconfirmed.</param>
public sealed record LoadedProductionFilament(Guid VariantId, Guid FilamentId, string MaterialType, string ColorHex,
    string ColorName, Guid SlotId, int ExtruderIndex = 0, decimal? NozzleMm = null);
