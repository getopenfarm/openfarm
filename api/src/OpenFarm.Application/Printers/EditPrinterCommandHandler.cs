using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterCommandHandler(
    IPrinterConnectorFactory connectorFactory,
    IPrinterRepository printerRepository,
    IConnectionSecretRepository connectionSecretRepository,
    ISecretProtector secretProtector,
    IPrinterConnectionManager connectionManager,
    IMachineProfileRepository machineProfileRepository,
    IProcessProfileRepository processProfileRepository,
    IActivityLogWriter activityLogWriter) : ICommandHandler<EditPrinterCommand, Printer>
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

        // 009 US2 — machine/process profile selection, validated before any mutation lands (same
        // zero-partial-success philosophy as the identity-field and connection checks above).
        if (command.ProcessProfileId is not null && command.MachineProfileId is null)
        {
            throw new ProcessProfileRequiresMachineProfileException();
        }

        MachineProfile? machineProfile = null;
        if (command.MachineProfileId is { } machineProfileId)
        {
            machineProfile = await machineProfileRepository.GetByIdAsync(machineProfileId, cancellationToken)
                ?? throw new InvalidProfileReferenceException("machine", machineProfileId);
        }

        ProcessProfile? processProfile = null;
        if (command.ProcessProfileId is { } processProfileId)
        {
            processProfile = await processProfileRepository.GetByIdAsync(processProfileId, cancellationToken)
                ?? throw new InvalidProfileReferenceException("process", processProfileId);

            if (processProfile.Origin != ProfileOrigin.System)
            {
                throw new InvalidProfileReferenceException("process", processProfileId);
            }

            if (processProfile.CompatibleMachineNames is null || !processProfile.CompatibleMachineNames.Contains(machineProfile!.Name, StringComparer.Ordinal))
            {
                throw new ProcessProfileIncompatibleWithMachineException(processProfile.Name, machineProfile!.Name);
            }
        }

        var previousMachineProfileId = printer.MachineProfileId;
        var previousProcessProfileId = printer.ProcessProfileId;

        printer.Rename(command.Name);

        if (connectionChanged)
        {
            printer.EditConnection(command.IpAddress!);

            var encryptedAccessCode = secretProtector.Protect(command.AccessCode!);
            var secret = ConnectionSecret.Create(Organization.WellKnownId, printer.SerialNumber, encryptedAccessCode);
            await connectionSecretRepository.AddAsync(secret, cancellationToken);
        }

        if (machineProfile is not null)
        {
            printer.LinkMachineProfile(machineProfile.Id);
        }
        else
        {
            printer.UnlinkMachineProfile();
        }

        if (processProfile is not null)
        {
            printer.LinkProcessProfile(processProfile.Id);
        }
        else
        {
            printer.UnlinkProcessProfile();
        }

        await printerRepository.SaveChangesAsync(cancellationToken);

        if (connectionChanged)
        {
            // Force a reconnect so the running connection loop picks up the new IP/access code.
            connectionManager.StopConnection(printer.Id);
            connectionManager.StartConnection(printer.Id, CancellationToken.None);
        }

        // FR-016 — granular events alongside the base PrinterEdited event the endpoint's
        // IActivityLoggable/global post-processor already writes for every successful call here.
        // Written directly (not via the post-processor, which only ever writes one event type per
        // request) only when the aspect actually changed — resending the same already-linked id on
        // every save (the printer form's own "submit full current state" convention) must not spam
        // a Linked event with no real change.
        if (printer.MachineProfileId != previousMachineProfileId)
        {
            var eventType = printer.MachineProfileId is not null ? "PrinterMachineProfileLinked" : "PrinterMachineProfileUnlinked";
            await activityLogWriter.WriteAsync(
                ActivityLogEntry.Create(printer.OrganizationId, "Printer", printer.Id.ToString(), eventType, command.ActorId, command.ActorType),
                cancellationToken);
        }

        if (printer.ProcessProfileId != previousProcessProfileId)
        {
            var eventType = printer.ProcessProfileId is not null ? "PrinterProcessProfileLinked" : "PrinterProcessProfileUnlinked";
            await activityLogWriter.WriteAsync(
                ActivityLogEntry.Create(printer.OrganizationId, "Printer", printer.Id.ToString(), eventType, command.ActorId, command.ActorType),
                cancellationToken);
        }

        return printer;
    }
}
