namespace OpenFarm.Infrastructure.Printers;

/// <summary>A start request may have reached the printer; telemetry must settle it before retry.</summary>
internal class PrinterStartUnconfirmedException(Exception inner)
    : IOException("The print start is unconfirmed. Await printer telemetry before retrying.", inner);
