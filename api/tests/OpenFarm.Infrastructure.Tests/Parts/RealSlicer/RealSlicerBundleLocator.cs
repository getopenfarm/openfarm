using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

/// <summary>
/// Locates a real, locally-installed slicer bundle for the golden-file test suite (never CI —
/// no runner installs a real slicer binary). Reuses the same production scanning pieces
/// (<see cref="BundledSlicerScanner"/>/<see cref="HostPlatform"/>) rather than re-walking directories.
/// </summary>
internal static class RealSlicerBundleLocator
{
    private const string BundleRootEnvVar = "OPENFARM_TEST_SLICER_BUNDLE_ROOT";

    public static (AvailableSlicer? Available, string Reason) TryResolve(SlicerEngine engine, string version)
    {
        var root = ResolveBundleRoot();
        if (root is null)
        {
            return (null, $"No real slicer bundle found — set {BundleRootEnvVar} or populate <repoRoot>/.slicers " +
                           "(see tools/provision-slicers and slicers.manifest.json).");
        }

        var scanner = new BundledSlicerScanner(
            Options.Create(new SlicerBundleOptions { RootDirectory = root }),
            NullLogger<BundledSlicerScanner>.Instance);
        var platform = new HostPlatform().Current;

        var available = scanner.Scan().FirstOrDefault(a =>
            a.Engine == engine && string.Equals(a.Version, version, StringComparison.Ordinal) && a.Platform == platform);

        return available is null
            ? (null, $"{engine} {version} for {platform} not found under '{root}'.")
            : (available, string.Empty);
    }

    /// <summary>
    /// Guardrail against the OrcaSlicer/BambuStudio SIGSEGV regression documented in
    /// slicers.manifest.json: a golden-file test case's requested version must match what's actually
    /// pinned there, or it may be silently exercising a different, crash-prone build. A mismatch is a
    /// stale/misconfigured test case, so it is asserted as a hard failure by the caller, not treated
    /// as an availability gap.
    /// </summary>
    public static string GetManifestPinnedVersion(SlicerEngine engine)
    {
        var manifestPath = FindManifestPath()
            ?? throw new InvalidOperationException("Could not locate slicers.manifest.json to verify the pinned slicer version.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var platformKey = new HostPlatform().Current.ToString();

        foreach (var entry in document.RootElement.GetProperty("slicers").EnumerateArray())
        {
            if (entry.GetProperty("engine").GetString() != engine.ToString())
            {
                continue;
            }

            return entry.GetProperty("builds").GetProperty(platformKey).GetProperty("version").GetString()
                ?? throw new InvalidOperationException($"slicers.manifest.json has no version for {engine} on {platformKey}.");
        }

        throw new InvalidOperationException($"slicers.manifest.json has no entry for {engine}.");
    }

    /// <summary>
    /// The real bundle leaf directory (<c>&lt;root&gt;/&lt;engineSlug&gt;/&lt;version&gt;/&lt;platformSlug&gt;</c>,
    /// <see cref="BundledSlicerScanner"/>'s own convention) for an already-resolved slicer — this is
    /// what a real install's own <c>resources/profiles/</c> lives under, as opposed to the small
    /// hand-curated fixtures in Profiles/Fixtures/VendorIndex used to unit-test the ingestion parser
    /// in isolation. Ingesting from here gets the real, complete, actually slice-ready vendor presets.
    /// </summary>
    public static string GetLeafDirectory(SlicerEngine engine, string version)
    {
        var root = ResolveBundleRoot()
            ?? throw new InvalidOperationException("No real slicer bundle root is configured.");
        var engineSlug = engine switch
        {
            SlicerEngine.OrcaSlicer => "orcaslicer",
            SlicerEngine.BambuStudio => "bambustudio",
            SlicerEngine.SuperSlicer => "superslicer",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
        };
        var platformSlug = new HostPlatform().Current switch
        {
            SlicerPlatform.Linux => "linux",
            SlicerPlatform.Windows => "windows",
            SlicerPlatform.MacOs => "macos",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
        };

        return Path.Combine(root, engineSlug, version, platformSlug);
    }

    private static string? ResolveBundleRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable(BundleRootEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var candidate = Path.Combine(repoRoot, ".slicers");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? FindManifestPath()
    {
        var repoRoot = FindRepoRoot();
        return repoRoot is null ? null : Path.Combine(repoRoot, "slicers.manifest.json");
    }

    // slicers.manifest.json lives at <repoRoot>/, so its presence marks the repo root regardless of
    // how deep the test assembly's own output directory ends up under api/tests/.../bin/....
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "slicers.manifest.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
