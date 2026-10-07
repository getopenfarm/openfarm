using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>Source identifies provenance. Cross-source support is directional and needs a real engine check.</summary>
public static class ProfileEngineCompatibility
{
    public static bool Supports(SlicerEngine engine, ProfileSourceFormat source) => engine switch
    {
        SlicerEngine.OrcaSlicer => source is ProfileSourceFormat.OrcaSlicer or ProfileSourceFormat.BambuStudio,
        SlicerEngine.BambuStudio => source == ProfileSourceFormat.BambuStudio,
        _ => false,
    };

    public static SlicerEngine NativeEngine(ProfileSourceFormat source) => source switch
    {
        ProfileSourceFormat.OrcaSlicer => SlicerEngine.OrcaSlicer,
        ProfileSourceFormat.BambuStudio => SlicerEngine.BambuStudio,
        _ => throw new SlicingProfileUnconfiguredException("Unsupported profile source."),
    };

    public static void EnsureSupported(SlicerEngine engine, params ProfileSourceFormat[] sources)
    {
        if (sources.Any(source => !Supports(engine, source)))
            throw new SlicingProfileUnconfiguredException($"One or more selected profiles cannot be used with {engine}. Orca-origin presets are not supported by Bambu Studio.");
    }
}
