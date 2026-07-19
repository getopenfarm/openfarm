using OpenFarm.Domain.Printers;

namespace OpenFarm.Domain.FilamentTracking;

/// <summary>
/// Open, extensible <see cref="FeedUnit.Kind"/> registry — mirrors
/// <see cref="OpenFarm.Domain.Printers.PrinterModelCatalog"/>'s shape (research.md §2). A new
/// hardware kind is a new entry here, never a migration.
/// </summary>
public static class FeedUnitKindCatalog
{
    public const string MultiMaterialUnit = "MultiMaterialUnit";
    public const string ExternalSpool = "ExternalSpool";
    public const string AutomaticFilamentReloader = "AutomaticFilamentReloader";

    private static readonly Dictionary<string, ReloadPolicy> Policies = new(StringComparer.Ordinal)
    {
        [MultiMaterialUnit] = ReloadPolicy.MatchRequired,
        [AutomaticFilamentReloader] = ReloadPolicy.Unconditional,
        [ExternalSpool] = ReloadPolicy.None,
    };

    /// <summary>Never throws — an unrecognized kind resolves to <see cref="ReloadPolicy.None"/> (research.md §2).</summary>
    public static ReloadPolicy Resolve(string kind) =>
        Policies.TryGetValue(kind, out var policy) ? policy : ReloadPolicy.None;

    /// <summary>Brand-specific terminology for a <see cref="MultiMaterialUnit"/>, computed at read time (research.md §5).</summary>
    public static string ResolveMultiMaterialUnitTerm(string brand) =>
        Enum.TryParse<PrinterBrand>(brand, ignoreCase: true, out var parsedBrand) && parsedBrand == PrinterBrand.Bambu
            ? "AMS"
            : "Multi-Material Unit";
}
