using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>Source identifies provenance. An engine uses presets of its own format only; another engine's preset is reached through <see cref="ProfileResolver"/>.</summary>
public static class ProfileEngineCompatibility
{
    public static bool Supports(SlicerEngine engine, ProfileSourceFormat source) => NativeFormat(engine) == source;

    public static SlicerEngine NativeEngine(ProfileSourceFormat source) => source switch
    {
        ProfileSourceFormat.OrcaSlicer => SlicerEngine.OrcaSlicer,
        ProfileSourceFormat.BambuStudio => SlicerEngine.BambuStudio,
        _ => throw new SlicingProfileUnconfiguredException("Unsupported profile source."),
    };

    /// <summary>The profile format an engine ships its own catalog in; <c>null</c> for an engine without profile presets.</summary>
    public static ProfileSourceFormat? NativeFormat(SlicerEngine engine) => engine switch
    {
        SlicerEngine.OrcaSlicer => ProfileSourceFormat.OrcaSlicer,
        SlicerEngine.BambuStudio => ProfileSourceFormat.BambuStudio,
        _ => null,
    };

    public static void EnsureSupported(SlicerEngine engine, params ProfileSourceFormat[] sources)
    {
        if (sources.Any(source => !Supports(engine, source)))
            throw new SlicingProfileUnconfiguredException($"A selected profile cannot be used with {engine}. {engine} uses its own profiles only.", ProfileResolutionOutcome.UnsupportedOrigin.ToString());
    }
}
