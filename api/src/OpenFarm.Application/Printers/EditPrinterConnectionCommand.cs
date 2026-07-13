namespace OpenFarm.Application.Printers;

public sealed record EditPrinterConnectionCommand(Guid PrinterId, string IpAddress, string AccessCode);
