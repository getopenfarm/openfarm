namespace OpenFarm.Application.Printers;

/// <param name="ExpectedSinceUtc">When the caller saw the printer start awaiting clear; a different value means another print ended since, so the plate the user cleared may not be this one.</param>
public sealed record MarkPrinterReadyCommand(Guid PrinterId, DateTimeOffset? ExpectedSinceUtc = null);

public sealed class PrinterChangedSinceReadException(Guid printerId)
    : Exception($"Printer {printerId} changed since it was read: another print may have ended. Read it again and ask the user whether that plate is clear.");
