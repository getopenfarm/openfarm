namespace OpenFarm.Application.Production;

/// <summary>
/// The one way a filament requirement's choice is named, "&lt;scope&gt; · &lt;color&gt;": "Any PETG · #000000",
/// "Any PETG · Any color", "Aliz PETG Basic · Any color", "Aliz PETG Basic · Black". The web's FilamentChoiceDialog mirrors it.
/// </summary>
public static class FilamentChoiceLabel
{
    public const string AnyColor = "Any color";

    /// <summary>No catalog choice: any brand of the material, in the file's colour or, with <paramref name="anyColor"/>, any colour.</summary>
    public static string Generic(string? materialType, string? colorHex, bool anyColor = false) =>
        $"{(string.IsNullOrWhiteSpace(materialType) ? "Any material" : $"Any {materialType}")} · {(anyColor || colorHex is null ? AnyColor : Hex(colorHex))}";

    /// <summary>A catalog filament in one colour, or in any colour when <paramref name="colorName"/> is null.</summary>
    public static string Catalog(string? vendorName, string? material, string? colorName = null) =>
        $"{string.Join(' ', new[] { vendorName, material }.Where(v => !string.IsNullOrWhiteSpace(v)))} · {colorName ?? AnyColor}";

    private static string Hex(string value) => $"#{value.TrimStart('#').ToUpperInvariant()}";
}
