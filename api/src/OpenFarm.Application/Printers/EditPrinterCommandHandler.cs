using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterCommandHandler(
    IPrinterConnectorFactory connectorFactory,
    IPrinterRepository printerRepository,
    IConnectionSecretRepository connectionSecretRepository,
    ISecretProtector secretProtector,
    IPrinterConnectionManager connectionManager) : ICommandHandler<EditPrinterCommand, Printer>
{
    public async Task<Printer> HandleAsync(EditPrinterCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        // FR-002/FR-010: zero-trust rejection — a supplied identity field is checked, never applied.
        // Rejected in full, before any mutation, so a bypassing direct-API caller can't partially
        // succeed by mixing a valid rename with a mismatched identity field.
        var mismatchedFields = new List<string>();
        if (command.Brand is not null && !string.Equals(command.Brand, printer.Brand, StringComparison.Ordinal))
        {
            mismatchedFields.Add("brand");
        }

        if (command.ModelCode is not null && !string.Equals(command.ModelCode, printer.ModelCode, StringComparison.Ordinal))
        {
            mismatchedFields.Add("modelCode");
        }

        if (command.SerialNumber is not null && !string.Equals(command.SerialNumber, printer.SerialNumber, StringComparison.Ordinal))
        {
            mismatchedFields.Add("serialNumber");
        }

        if (mismatchedFields.Count > 0)
        {
            throw new ImmutablePrinterFieldMismatchException(mismatchedFields);
        }

        // FR-003: presence of ipAddress+accessCode (not a diff against the stored value) gates
        // whether the verify-before-save connection change runs (research.md §2). Verification
        // happens before any mutation (including the rename) so a failed verification leaves the
        // printer entirely unchanged, not partially renamed.
        var connectionChanged = command.IpAddress is not null && command.AccessCode is not null;
        if (connectionChanged)
        {
            var details = new PrinterConnectionDetails(printer.Brand, printer.ModelCode, command.IpAddress!, printer.SerialNumber, command.AccessCode!);
            var verified = await connectorFactory.CreateConnector(details).VerifyAsync(details, cancellationToken);
            if (!verified)
            {
                throw new PrinterConnectionVerificationFailedException(command.IpAddress!);
            }
        }

        printer.Rename(command.Name);

        if (connectionChanged)
        {
            printer.EditConnection(command.IpAddress!);

            var encryptedAccessCode = secretProtector.Protect(command.AccessCode!);
            var secret = ConnectionSecret.Create(Organization.WellKnownId, printer.SerialNumber, encryptedAccessCode);
            await connectionSecretRepository.AddAsync(secret, cancellationToken);
        }

        await printerRepository.SaveChangesAsync(cancellationToken);

        if (connectionChanged)
        {
            // Force a reconnect so the running connection loop picks up the new IP/access code.
            connectionManager.StopConnection(printer.Id);
            connectionManager.StartConnection(printer.Id, CancellationToken.None);
        }

        return printer;
    }
}
