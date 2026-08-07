using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>Builds a live connector's <see cref="PrinterConnectionDetails"/> from a stored <see cref="Printer"/>, mirroring <c>PrinterConnectionBackgroundService.RunConnectionLoopAsync</c>'s own lookup — the one place any HTTP-request-scoped command handler needs this, so 014's remote-control handlers don't each re-implement it.</summary>
public sealed class ResolvePrinterConnectionDetails(IConnectionSecretRepository connectionSecrets, ISecretProtector secretProtector)
{
    public async Task<PrinterConnectionDetails?> ResolveAsync(Printer printer, CancellationToken cancellationToken)
    {
        var secret = await connectionSecrets.GetByPrinterSerialNumberAsync(printer.SerialNumber, cancellationToken);
        if (secret is null)
        {
            return null;
        }

        return new PrinterConnectionDetails(printer.Brand, printer.ModelCode, printer.IpAddress, printer.SerialNumber, secretProtector.Unprotect(secret.EncryptedAccessCode));
    }
}
