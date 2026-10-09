namespace OpenFarm.Domain.Printers;

public sealed record PrinterAlert(string Source, string Code, string Severity, string Message, string? TroubleshootingUrl = null);
