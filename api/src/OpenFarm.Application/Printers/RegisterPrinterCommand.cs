namespace OpenFarm.Application.Printers;

public sealed record RegisterPrinterCommand(
    string Name,
    string Brand,
    string ModelCode,
    string IpAddress,
    string SerialNumber,
    string AccessCode);
