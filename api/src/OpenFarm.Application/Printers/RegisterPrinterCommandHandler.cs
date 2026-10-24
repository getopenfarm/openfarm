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
        var typedSerial = !string.IsNullOrWhiteSpace(command.SerialNumber);
        if (typedSerial)
        {
            await EnsureNotRegisteredAsync(command.SerialNumber, cancellationToken);
        }

        // FR-001a: never trust only the client's earlier verify-connection call — re-run it here.
        var details = new PrinterConnectionDetails(command.Brand, command.ModelCode, command.IpAddress, command.SerialNumber, command.AccessCode);
        var connector = connectorFactory.CreateConnector(details);
        var verified = await connector.VerifyAsync(details, cancellationToken);
        if (!verified)
        {
            throw new PrinterConnectionVerificationFailedException(command.IpAddress);
        }

        // A printer that reports its own serial (Snapmaker) is registered under it; the form never asks for it.
        var serialNumber = command.SerialNumber;
        if (!typedSerial)
        {
            serialNumber = await connector.ReadSerialNumberAsync(details, cancellationToken);
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new PrinterConnectionVerificationFailedException(command.IpAddress);
            }

            await EnsureNotRegisteredAsync(serialNumber, cancellationToken);
        }

        var printer = Printer.Create(
            command.Name,
            command.Brand,
            command.ModelCode,
            command.IpAddress,
            serialNumber);

        await printerRepository.AddAsync(printer, cancellationToken);

        var encryptedAccessCode = secretProtector.Protect(command.AccessCode);
        var secret = ConnectionSecret.Create(serialNumber, encryptedAccessCode);
        await connectionSecretRepository.AddAsync(secret, cancellationToken);

        connectionManager.StartConnection(printer.Id, CancellationToken.None);

        return printer;
    }

    private async Task EnsureNotRegisteredAsync(string serialNumber, CancellationToken cancellationToken)
    {
        var existing = await printerRepository.GetBySerialNumberAsync(serialNumber, cancellationToken);
        if (existing is not null)
        {
            throw new DuplicatePrinterSerialNumberException(serialNumber, existing.Name);
        }
    }
}
