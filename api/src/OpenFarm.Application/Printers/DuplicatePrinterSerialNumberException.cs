namespace OpenFarm.Application.Printers;

public sealed class DuplicatePrinterSerialNumberException(string serialNumber, string conflictingPrinterName)
    : Exception($"A printer named '{conflictingPrinterName}' is already registered with serial number '{serialNumber}'.")
{
    public string SerialNumber { get; } = serialNumber;

    public string ConflictingPrinterName { get; } = conflictingPrinterName;
}
