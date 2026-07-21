using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// A process preset can only ever be valid paired to a machine from the same slicer family — Orca's
// own compatibility check has zero tolerance for a cross-vendor guess, so a cross-format pairing
// (e.g. a Bambu Studio process paired to an OrcaSlicer machine) is rejected outright before ever
// reaching the CLI, even though this can't catch a same-format-different-model mismatch (only the
// real CLI's own compatibility gate can detect that, at slice time).
public sealed class ProcessProfileSourceFormatMismatchException(ProfileSourceFormat machineFormat, ProfileSourceFormat processFormat)
    : Exception($"The machine profile's source format ({machineFormat}) does not match the process profile's source format ({processFormat}).")
{
    public ProfileSourceFormat MachineFormat { get; } = machineFormat;

    public ProfileSourceFormat ProcessFormat { get; } = processFormat;
}
