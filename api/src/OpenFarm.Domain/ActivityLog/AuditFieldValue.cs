using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenFarm.Domain.ActivityLog;

/// <summary>Secret protection at the audit boundary, including nested structured snapshots.</summary>
public static class AuditFieldValue
{
    private static readonly string[] SecretParts = ["password", "token", "secret", "credential", "accesscode", "apikey", "authorization", "connectionstring"];

    public static object? Sanitize(string field, object? value)
    {
        if (value is null) return null;
        if (IsSecret(field)) return "[redacted]";
        if (value is Enum enumValue) return enumValue.ToString();
        JsonNode? node;
        if (value is string text && (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')))
        {
            try { node = JsonNode.Parse(text); }
            catch (JsonException) { return "[unavailable]"; }
        }
        else node = JsonSerializer.SerializeToNode(value);
        Redact(node);
        return node;
    }

    public static string? SanitizeSnapshot(string? value)
    {
        if (value is null || !(value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('['))) return value;
        return Sanitize("Snapshot", value) is JsonNode node ? node.ToJsonString() : "[unavailable]";
    }

    private static bool IsSecret(string field)
    {
        var leaf = field.Split('.', ':')[^1].Replace("_", "", StringComparison.Ordinal);
        return SecretParts.Any(part => leaf.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
            {
                if (IsSecret(pair.Key)) obj[pair.Key] = pair.Value is null ? null : JsonValue.Create("[redacted]");
                else Redact(pair.Value);
            }
        else if (node is JsonArray array)
            foreach (var item in array) Redact(item);
    }
}
