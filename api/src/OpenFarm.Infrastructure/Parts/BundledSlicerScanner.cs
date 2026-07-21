using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Walks <see cref="SlicerBundleOptions.RootDirectory"/> into the
/// <c>&lt;engineSlug&gt;/&lt;version&gt;/&lt;platformSlug&gt;</c> convention (research.md §1),
/// producing one <see cref="AvailableSlicer"/> per present leaf. Presence is availability — no
/// execution/validation (FR-002). Run once, at startup, by <see cref="AvailableSlicerCatalog"/>.
/// </summary>
public sealed partial class BundledSlicerScanner(IOptions<SlicerBundleOptions> options, ILogger<BundledSlicerScanner> logger)
{
    private static readonly Dictionary<string, SlicerEngine> EngineSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orcaslicer"] = SlicerEngine.OrcaSlicer,
        ["bambustudio"] = SlicerEngine.BambuStudio,
        ["superslicer"] = SlicerEngine.SuperSlicer,
    };

    private static readonly Dictionary<string, SlicerPlatform> PlatformSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["linux"] = SlicerPlatform.Linux,
        ["windows"] = SlicerPlatform.Windows,
        ["macos"] = SlicerPlatform.MacOs,
    };

    public IReadOnlyList<AvailableSlicer> Scan()
    {
        var root = options.Value.RootDirectory;
        var results = new List<AvailableSlicer>();

        if (!Directory.Exists(root))
        {
            LogRootDirectoryMissing(logger, root);
            return results;
        }

        // Duplicate leaves (e.g. differently-cased engine slug directories resolving to the same
        // engine) are de-duplicated deterministically by stable ordering of the enumerated paths —
        // the first one wins, later ones are logged and skipped (spec Edge Cases).
        var seenKeys = new HashSet<(SlicerEngine Engine, string Version, SlicerPlatform Platform)>();

        foreach (var engineDir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (!EngineSlugs.TryGetValue(Path.GetFileName(engineDir), out var engine))
            {
                continue;
            }

            foreach (var versionDir in Directory.EnumerateDirectories(engineDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                var version = Path.GetFileName(versionDir);

                foreach (var platformDir in Directory.EnumerateDirectories(versionDir).OrderBy(d => d, StringComparer.Ordinal))
                {
                    if (!PlatformSlugs.TryGetValue(Path.GetFileName(platformDir), out var platform))
                    {
                        continue;
                    }

                    var key = (engine, version, platform);
                    if (!seenKeys.Add(key))
                    {
                        LogDuplicateLeaf(logger, engine, version, platform, platformDir);
                        continue;
                    }

                    var executablePath = SlicerExecutableResolver.Resolve(platformDir, platform);
                    if (executablePath is null)
                    {
                        LogUnresolvableLeaf(logger, engine, version, platform, platformDir);
                        continue;
                    }

                    results.Add(new AvailableSlicer(engine, version, platform, executablePath));
                }
            }
        }

        if (results.Count == 0)
        {
            LogNoSlicersFound(logger, root);
        }

        return results;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bundled slicer root directory '{Path}' does not exist — no slicers will be available until it is created and populated (e.g. via tools/provision-slicers).")]
    private static partial void LogRootDirectoryMissing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bundled slicer root directory '{Path}' exists but contains no usable (engine, version, platform) leaves — no slicers will be available.")]
    private static partial void LogNoSlicersFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate bundled slicer leaf for {Engine} {Version} ({Platform}) at '{Path}' — ignored, an earlier leaf already claimed this entry.")]
    private static partial void LogDuplicateLeaf(ILogger logger, SlicerEngine engine, string version, SlicerPlatform platform, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bundled slicer leaf for {Engine} {Version} ({Platform}) at '{Path}' has no resolvable executable — ignored.")]
    private static partial void LogUnresolvableLeaf(ILogger logger, SlicerEngine engine, string version, SlicerPlatform platform, string path);
}
