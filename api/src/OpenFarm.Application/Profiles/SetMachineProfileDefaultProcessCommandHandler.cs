using OpenFarm.Application.Common;

namespace OpenFarm.Application.Profiles;

// A process preset is only ever valid paired to one specific machine (Orca's real machine/process
// compatibility check has zero tolerance for a wrong guess — research.md) — this pairing is always
// an explicit operator action, never inferred at import time. Rejects a cross-format pairing
// outright (a same-format-different-model mismatch still can't be caught here, only by the real CLI
// at slice time).
public sealed class SetMachineProfileDefaultProcessCommandHandler(
    IMachineProfileRepository machineProfileRepository, IProcessProfileRepository processProfileRepository)
    : ICommandHandler<SetMachineProfileDefaultProcessCommand, MachineProfileDefaultProcessResult>
{
    public async Task<MachineProfileDefaultProcessResult> HandleAsync(SetMachineProfileDefaultProcessCommand command, CancellationToken cancellationToken)
    {
        var machineProfile = await machineProfileRepository.GetByIdAsync(command.MachineProfileId, cancellationToken)
            ?? throw new MachineProfileNotFoundException(command.MachineProfileId);

        var processProfile = await processProfileRepository.GetByIdAsync(command.ProcessProfileId, cancellationToken)
            ?? throw new ProcessProfileNotFoundException(command.ProcessProfileId);

        if (machineProfile.SourceFormat != processProfile.SourceFormat)
        {
            throw new ProcessProfileSourceFormatMismatchException(machineProfile.SourceFormat, processProfile.SourceFormat);
        }

        machineProfile.SetDefaultProcessProfile(processProfile.Id);
        await machineProfileRepository.SaveChangesAsync(cancellationToken);

        return new MachineProfileDefaultProcessResult(machineProfile.Id, processProfile.Id);
    }
}
