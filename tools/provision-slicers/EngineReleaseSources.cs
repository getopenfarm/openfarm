namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary>An official GitHub repo + the upstream asset-name pattern for one <c>(engine, version, platform)</c> (research.md §8).</summary>
public sealed record ReleaseSource(string Repo, string AssetName);

/// <summary><see cref="Source"/> is <c>null</c> exactly when <see cref="Error"/> is set.</summary>
public sealed record ReleaseSourceResult(ReleaseSource? Source, string? Error)
{
    public bool Success => Source is not null;

    public static ReleaseSourceResult Successful(ReleaseSource source) => new(source, null);

    public static ReleaseSourceResult Failure(string error) => new(null, error);
}

/// <summary>
/// Per-engine official GitHub release source + per-platform asset-name resolution (research.md §8).
/// An unresolvable combination (e.g. a platform an engine doesn't officially publish) is reported as
/// a typed per-entry failure — it never throws, so one bad manifest entry can't abort the run
/// (FR-015).
/// </summary>
public static class EngineReleaseSources
{
    private static readonly IReadOnlyDictionary<Engine, string> Repos = new Dictionary<Engine, string>
    {
        [Engine.OrcaSlicer] = "SoftFever/OrcaSlicer",
        [Engine.BambuStudio] = "bambulab/BambuStudio",
        [Engine.SuperSlicer] = "supermerill/SuperSlicer",
    };

    public static ReleaseSourceResult Resolve(Engine engine, string version, Platform platform)
    {
        if (!Repos.TryGetValue(engine, out var repo))
        {
            return ReleaseSourceResult.Failure($"No release source is known for engine {engine}.");
        }

        var assetName = engine switch
        {
            Engine.OrcaSlicer => platform switch
            {
                Platform.Linux => $"OrcaSlicer_Linux_AppImage_Ubuntu2404_V{version}.AppImage",
                Platform.Windows => $"OrcaSlicer_Windows_V{version}.zip",
                Platform.MacOs => $"OrcaSlicer_Mac_arm64_V{version}.dmg",
                _ => null,
            },
            Engine.BambuStudio => platform switch
            {
                Platform.Linux => $"Bambu_Studio_linux_public_v{version}.AppImage",
                Platform.Windows => $"Bambu_Studio_windows_public_v{version}.zip",
                Platform.MacOs => $"Bambu_Studio_mac_public_v{version}.dmg",
                _ => null,
            },
            // SuperSlicer does not consistently publish official macOS builds for every release —
            // a genuine unresolvable combination, not a bug in this resolver.
            Engine.SuperSlicer => platform switch
            {
                Platform.Linux => $"SuperSlicer-ubuntu_{version}-linux-x64-GTK3.AppImage",
                Platform.Windows => $"SuperSlicer_{version}-win64.zip",
                _ => null,
            },
            _ => null,
        };

        return assetName is null
            ? ReleaseSourceResult.Failure($"{engine} does not publish a known asset for platform {platform}.")
            : ReleaseSourceResult.Successful(new ReleaseSource(repo, assetName));
    }
}
