namespace OpenFarm.Application.Printers;

public sealed class PrinterConnectionVerificationFailedException(string ipAddress)
    : Exception($"Could not authenticate with the printer at {ipAddress}. Check the IP address and LAN access code.")
{
    public string IpAddress { get; } = ipAddress;
}
