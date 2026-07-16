namespace OpenFarm.Application.Profiles;

public sealed class MachineProfileNotFoundException(Guid machineProfileId)
    : Exception($"Machine profile '{machineProfileId}' was not found.")
{
    public Guid MachineProfileId { get; } = machineProfileId;
}
