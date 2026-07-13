namespace OpenFarm.Domain.Printers;

public sealed class InvalidPrinterStatusTransitionException(PrinterStatus from, PrinterStatus to)
    : Exception($"Cannot manually transition a printer from '{from}' to '{to}'.")
{
    public PrinterStatus From { get; } = from;

    public PrinterStatus To { get; } = to;
}
