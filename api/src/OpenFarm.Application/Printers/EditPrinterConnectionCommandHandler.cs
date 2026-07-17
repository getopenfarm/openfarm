using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterConnectionCommandHandler(
    IPrinterConnectorFactory connectorFactory,
    IPrinterRepository printerRepository,
    IConnectionSecretRepository connectionSecretRepository,
    ISecretProtector secretProtector,
    IPrinterConnectionManager connectionManager) : ICommandHandler<EditPrinterConnectionCommand, Printer>
{
    public async Task<Printer> HandleAsync(EditPrinterConnectionCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        // FR-002: same verify-before-save gate as registration.
        var details = new PrinterConnectionDetails(printer.Brand, printer.ModelCode, command.IpAddress, printer.SerialNumber, command.AccessCode);
        var verified = await connectorFactory.CreateConnector(details).VerifyAsync(details, cancellationToken);
        if (!verified)
        {
            throw new PrinterConnectionVerificationFailedException(command.IpAddress);
        }

        printer.EditConnection(command.IpAddress);
        await printerRepository.SaveChangesAsync(cancellationToken);

        var encryptedAccessCode = secretProtector.Protect(command.AccessCode);
        var secret = ConnectionSecret.Create(Organization.WellKnownId, printer.SerialNumber, encryptedAccessCode);
        await connectionSecretRepository.AddAsync(secret, cancellationToken);

        // Force a reconnect so the running connection loop picks up the new IP/access code.
        connectionManager.StopConnection(printer.Id);
        connectionManager.StartConnection(printer.Id, CancellationToken.None);

        return printer;
    }
}
