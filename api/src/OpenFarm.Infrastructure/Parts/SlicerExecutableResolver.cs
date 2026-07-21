using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Resolves the actual executable within one bundle leaf, per engine-family platform convention
/// (research.md §4/§9): an extracted Linux AppImage's <c>AppRun</c> entry point, a Windows console
/// <c>*.exe</c>, or a macOS <c>.app/Contents/MacOS/…</c> binary. Run once by
/// <see cref="BundledSlicerScanner"/> at discovery time — the resolved path is cached on
/// <see cref="AvailableSlicer.ExecutablePath"/>, never re-resolved per slice.
/// </summary>
internal static class SlicerExecutableResolver
{
    public static string? Resolve(string leafDirectory, SlicerPlatform platform) => platform switch
    {
        SlicerPlatform.Linux => ResolveLinux(leafDirectory),
        SlicerPlatform.Windows => ResolveWindows(leafDirectory),
        SlicerPlatform.MacOs => ResolveMacOs(leafDirectory),
        _ => null,
    };

    private static string? ResolveLinux(string leafDirectory)
    {
        var appRun = Path.Combine(leafDirectory, "AppRun");
        if (File.Exists(appRun))
        {
            return appRun;
        }

        return Directory.Exists(leafDirectory)
            ? Directory.EnumerateFiles(leafDirectory).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    private static string? ResolveWindows(string leafDirectory) =>
        Directory.Exists(leafDirectory)
            ? Directory.EnumerateFiles(leafDirectory, "*.exe", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;

    private static string? ResolveMacOs(string leafDirectory)
    {
        if (!Directory.Exists(leafDirectory))
        {
            return null;
        }

        var appBundle = Directory.EnumerateDirectories(leafDirectory, "*.app", SearchOption.TopDirectoryOnly)
            .OrderBy(d => d, StringComparer.Ordinal)
            .FirstOrDefault();
        if (appBundle is null)
        {
            return null;
        }

        var macOsDir = Path.Combine(appBundle, "Contents", "MacOS");
        return Directory.Exists(macOsDir)
            ? Directory.EnumerateFiles(macOsDir).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }
}
