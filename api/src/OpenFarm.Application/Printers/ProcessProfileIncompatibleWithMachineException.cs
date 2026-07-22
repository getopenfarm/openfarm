namespace OpenFarm.Application.Printers;

// FR-006 — a process profile is only ever compatible with the exact machine presets the vendor
// shipped it for (research.md §0 point 2's CompatibleMachineNames); no tolerance for a wrong guess.
public sealed class ProcessProfileIncompatibleWithMachineException(string processProfileName, string machineProfileName)
    : Exception($"'{processProfileName}' is not compatible with '{machineProfileName}'.")
{
    public string ProcessProfileName { get; } = processProfileName;

    public string MachineProfileName { get; } = machineProfileName;
}
