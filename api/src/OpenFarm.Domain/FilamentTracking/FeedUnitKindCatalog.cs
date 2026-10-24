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
}
