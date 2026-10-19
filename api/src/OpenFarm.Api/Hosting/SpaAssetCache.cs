using System.Text.RegularExpressions;

namespace OpenFarm.Api.Hosting;

internal static partial class SpaAssetCache
{
    public static bool IsFingerprintedAsset(PathString path) => FingerprintedAsset().IsMatch(path.Value ?? "");

    [GeneratedRegex(@"^/assets/[^/]+-[A-Za-z0-9_-]{8,}\.[^/]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintedAsset();
}
