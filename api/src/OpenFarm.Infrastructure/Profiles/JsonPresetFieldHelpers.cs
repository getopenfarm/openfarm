using System.Globalization;
using System.Text.Json;

namespace OpenFarm.Infrastructure.Profiles;

/// <summary>
/// Small, tolerant field-extraction helpers shared by every reader of a real OrcaSlicer/Bambu Studio
/// preset's raw JSON — a scalar field and a single-element array carrying the same scalar are treated
/// identically (real vendor JSON uses both shapes for the same logical field across different keys),
/// and a missing/wrong-shaped field degrades to "absent" rather than throwing (research.md's
/// "tolerate, don't fail" philosophy, shared by <see cref="ProfileBundleParser"/> and
/// <see cref="VendorProfileIndexReader"/>).
/// </summary>
internal static class JsonPresetFieldHelpers
{
    public static string? TryGetScalarOrFirstArrayElement(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array when value.GetArrayLength() > 0 => value[0].ValueKind switch
            {
                JsonValueKind.String => value[0].GetString(),
                JsonValueKind.Number => value[0].GetRawText(),
                _ => null,
            },
            _ => null,
        };
    }

    public static bool TryGetStringArray(JsonElement root, string propertyName, out IReadOnlyList<string> values)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            values = value.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
            return values.Count > 0;
        }

        values = [];
        return false;
    }

    public static double? TryGetDouble(JsonElement root, string propertyName)
    {
        var raw = TryGetScalarOrFirstArrayElement(root, propertyName);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static int? TryGetInt(JsonElement root, string propertyName)
    {
        var raw = TryGetScalarOrFirstArrayElement(root, propertyName);
        return raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDouble)
                ? (int)Math.Round(asDouble)
                : null;
    }

    public static bool? TryGetBool(JsonElement root, string propertyName)
    {
        var raw = TryGetScalarOrFirstArrayElement(root, propertyName);
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
    }
}
