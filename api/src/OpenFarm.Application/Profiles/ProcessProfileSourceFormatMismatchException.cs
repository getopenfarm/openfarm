using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Legacy error contract retained for callers. Engine support is now checked by
// ProfileEngineCompatibility; source inequality alone does not establish incompatibility.
public sealed class ProcessProfileSourceFormatMismatchException(ProfileSourceFormat machineFormat, ProfileSourceFormat processFormat)
    : Exception($"The machine profile's source format ({machineFormat}) does not match the process profile's source format ({processFormat}).")
{
    public ProfileSourceFormat MachineFormat { get; } = machineFormat;

    public ProfileSourceFormat ProcessFormat { get; } = processFormat;
}
