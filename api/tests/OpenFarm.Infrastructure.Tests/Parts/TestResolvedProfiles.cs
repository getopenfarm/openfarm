using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Infrastructure.Tests.Parts;

/// <summary>A resolved (machine, process, filament) set for tests that only need profiles to exist, not to come from a catalog.</summary>
internal static class TestResolvedProfiles
{
    public static ResolvedSlicingProfiles Create(
        SlicerRef pin, string machineJson = "{}", string processJson = "{}", string filamentJson = "{}",
        ProfileSourceFormat format = ProfileSourceFormat.OrcaSlicer) =>
        new(pin, machineJson, processJson, filamentJson,
            Preset(ProfileKind.Machine, "Machine", format, pin), Preset(ProfileKind.Process, "Process", format, pin),
            Preset(ProfileKind.Filament, "Filament", format, pin));

    private static ResolvedPreset Preset(ProfileKind kind, string name, ProfileSourceFormat format, SlicerRef pin) =>
        new(kind, Guid.NewGuid(), name, Guid.NewGuid(), name, format, pin.Version, SettingId: null, VendorName: "Bambulab", ProfileResolutionOutcome.Exact);
}
