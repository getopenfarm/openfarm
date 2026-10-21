using System.Text.Json;

namespace OpenFarm.Infrastructure.Profiles;

/// <summary>
/// Structural JSON equality for ingestion's change detection. Postgres <c>jsonb</c> hands back a canonical text
/// (its own key order and spacing, duplicate keys collapsed to the last one), so comparing a stored row's text
/// with the vendor file's text would report every row as changed on every run.
/// </summary>
internal static class JsonSemanticEquality
{
    public static bool Equal(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var leftDocument = JsonDocument.Parse(left);
            using var rightDocument = JsonDocument.Parse(right);
            return Equal(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Equal(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProperties = LastWins(left);
                var rightProperties = LastWins(right);
                return leftProperties.Count == rightProperties.Count
                    && leftProperties.All(p => rightProperties.TryGetValue(p.Key, out var other) && Equal(p.Value, other));
            case JsonValueKind.Array:
                return left.GetArrayLength() == right.GetArrayLength()
                    && left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => Equal(pair.First, pair.Second));
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return left.GetRawText() == right.GetRawText()
                    || (left.TryGetDecimal(out var leftNumber) && right.TryGetDecimal(out var rightNumber) && leftNumber == rightNumber);
            default:
                return true; // true / false / null: the kind is the value
        }
    }

    private static Dictionary<string, JsonElement> LastWins(JsonElement obj)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            properties[property.Name] = property.Value;
        }

        return properties;
    }
}
