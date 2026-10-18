using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed class RegisterPrinterCommandHandler(
    IPrinterConnectorFactory connectorFactory,
    IPrinterRepository printerRepository,
    IConnectionSecretRepository connectionSecretRepository,
    ISecretProtector secretProtector,
    IPrinterConnectionManager connectionManager) : ICommandHandler<RegisterPrinterCommand, Printer>
{
    public async Task<Printer> HandleAsync(RegisterPrinterCommand command, CancellationToken cancellationToken)
    {
        var existing = await printerRepository.GetBySerialNumberAsync(command.SerialNumber, cancellationToken);
        if (existing is not null)
        {
            throw new DuplicatePrinterSerialNumberException(command.SerialNumber, existing.Name);
        }

        // FR-001a: never trust only the client's earlier verify-connection call — re-run it here.
        var details = new PrinterConnectionDetails(command.Brand, command.ModelCode, command.IpAddress, command.SerialNumber, command.AccessCode);
        var verified = await connectorFactory.CreateConnector(details).VerifyAsync(details, cancellationToken);
        if (!verified)
        {
            throw new PrinterConnectionVerificationFailedException(command.IpAddress);
        }

        var printer = Printer.Create(
            command.Name,
            command.Brand,
            command.ModelCode,
            command.IpAddress,
            command.SerialNumber);

        await printerRepository.AddAsync(printer, cancellationToken);

        var encryptedAccessCode = secretProtector.Protect(command.AccessCode);
        var secret = ConnectionSecret.Create(command.SerialNumber, encryptedAccessCode);
        await connectionSecretRepository.AddAsync(secret, cancellationToken);

        connectionManager.StartConnection(printer.Id, CancellationToken.None);

        return printer;
    }
}
