using System.Text.Json;

namespace OpenFarm.Infrastructure.Profiles;

/// <summary>
/// Locates a bundled slicer leaf's <c>resources/profiles/</c> directory (research.md's "Locating
/// resources/profiles") by recursive search rather than a hardcoded relative path — mirroring
/// <c>SlicerExecutableResolver</c>'s own precedent that the three platform archive shapes
/// <c>tools/provision-slicers</c> produces do not share one fixed internal layout. A directory is a
/// match purely by name (<c>profiles</c>, case-insensitive); the vendor index files inside it are
/// then identified by shape (top-level <c>machine_model_list</c>/<c>machine_list</c>/
/// <c>process_list</c>/<c>filament_list</c> keys), not by name, since a real bundle ships one such
/// file per vendor (<c>BBL.json</c>, <c>Creality.json</c>, …) with no fixed filename convention.
/// </summary>
public static class ResourcesProfilesLocator
{
    private static readonly string[] VendorIndexKeys =
        ["machine_model_list", "machine_list", "process_list", "filament_list"];

    /// <summary>Finds the leaf's <c>profiles</c> directory, or <c>null</c> if none exists anywhere under it.</summary>
    public static string? FindProfilesDirectory(string bundleLeafPath)
    {
        if (!Directory.Exists(bundleLeafPath))
        {
            return null;
        }

        return SearchRecursive(bundleLeafPath);
    }

    /// <summary>
    /// Every vendor index file directly inside the leaf's <c>profiles</c> directory, sorted for
    /// deterministic ingestion ordering. Empty (not an error) when no <c>profiles</c> directory
    /// exists or it contains no vendor-index-shaped JSON — the caller reports that as a clear
    /// provisioning-step outcome, not a locator-level failure.
    /// </summary>
    public static IReadOnlyList<string> FindVendorIndexFiles(string bundleLeafPath)
    {
        var profilesDirectory = FindProfilesDirectory(bundleLeafPath);
        if (profilesDirectory is null)
        {
            return [];
        }

        return Directory.EnumerateFiles(profilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(LooksLikeVendorIndex)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static string? SearchRecursive(string directory)
    {
        IReadOnlyList<string> subDirectories;
        try
        {
            subDirectories = Directory.EnumerateDirectories(directory).OrderBy(d => d, StringComparer.Ordinal).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var subDirectory in subDirectories)
        {
            if (Path.GetFileName(subDirectory).Equals("profiles", StringComparison.OrdinalIgnoreCase))
            {
                return subDirectory;
            }
        }

        foreach (var subDirectory in subDirectories)
        {
            var found = SearchRecursive(subDirectory);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool LooksLikeVendorIndex(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && VendorIndexKeys.Any(key => root.TryGetProperty(key, out _));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }
}
