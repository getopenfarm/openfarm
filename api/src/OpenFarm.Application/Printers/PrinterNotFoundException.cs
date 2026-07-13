namespace OpenFarm.Application.Printers;

public sealed class PrinterNotFoundException(Guid printerId) : Exception($"Printer '{printerId}' was not found.")
{
    public Guid PrinterId { get; } = printerId;
}
