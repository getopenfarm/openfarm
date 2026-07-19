using OpenFarm.Application.Common;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>Wires the Printer ↔ Machine Profile link (FR-005b) — nozzle diameter is read from a linked profile, not duplicated onto Printer.</summary>
public sealed class LinkPrinterMachineProfileCommandHandler(
    IPrinterRepository printerRepository, IMachineProfileRepository machineProfileRepository)
    : ICommandHandler<LinkPrinterMachineProfileCommand, Printer>
{
    public async Task<Printer> HandleAsync(LinkPrinterMachineProfileCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        if (command.MachineProfileId is { } profileId)
        {
            _ = await machineProfileRepository.GetByIdAsync(profileId, cancellationToken)
                ?? throw new MachineProfileNotFoundException(profileId);

            printer.LinkMachineProfile(profileId);
        }
        else
        {
            printer.UnlinkMachineProfile();
        }

        await printerRepository.SaveChangesAsync(cancellationToken);
        return printer;
    }
}
