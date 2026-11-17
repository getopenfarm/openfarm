using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenFarm.Infrastructure.Auth;

public sealed class ScimException(int status, string detail, string? scimType = null) : Exception(detail)
{
    public int Status { get; } = status;
    public string? ScimType { get; } = scimType;
}

public static partial class ScimProtocol
{
    public const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string GroupSchema = "urn:ietf:params:scim:schemas:core:2.0:Group";
    public const string PatchSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    public const string ListSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const string MediaType = "application/scim+json";

    public static JsonNode? Get(JsonObject obj, string key) => obj.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    public static bool Has(JsonObject obj, string key) => obj.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    public static void Set(JsonObject obj, string key, JsonNode? value)
    {
        var old = obj.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Key;
        if (old is not null) obj.Remove(old);
        obj[key] = value?.DeepClone();
    }

    public static string? Text(JsonObject obj, string key, int max = 256)
    {
        var node = Get(obj, key);
        if (node is null) return null;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var result) || result.Length > max || result.Any(char.IsControl))
            throw new ScimException(400, $"Invalid {key}.", "invalidValue");
        return result;
    }

    public static string Required(JsonObject obj, string key, int max = 256) =>
        Text(obj, key, max) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ScimException(400, $"{key} is required.", "invalidValue");

    public static bool? Boolean(JsonObject obj, string key)
    {
        var node = Get(obj, key);
        if (node is null) return null;
        if (node is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
        throw new ScimException(400, $"{key} must be a boolean.", "invalidValue");
    }

    public static void RequireSchema(JsonObject obj, string schema)
    {
        if (Get(obj, "schemas") is not JsonArray schemas || !schemas.Any(n => n is JsonValue value && value.TryGetValue<string>(out var s) && s == schema))
            throw new ScimException(400, "The required SCIM schema is missing.", "invalidValue");
    }

    public static (string Attribute, string Value)? Filter(string? filter, bool group)
    {
        if (string.IsNullOrEmpty(filter)) return null;
        if (filter.Length > 2048) throw new ScimException(400, "Filter is too long.", "invalidFilter");
        var match = EqualityFilter().Match(filter);
        if (!match.Success) throw new ScimException(400, "Only equality filters on id, externalId, userName or displayName are supported.", "invalidFilter");
        var attribute = match.Groups[1].Value.ToUpperInvariant();
        if (attribute == (group ? "USERNAME" : "DISPLAYNAME")) throw new ScimException(400, "Unsupported filter attribute.", "invalidFilter");
        try { return (attribute, JsonSerializer.Deserialize<string>(match.Groups[2].Value)!); }
        catch (JsonException) { throw new ScimException(400, "Invalid filter string.", "invalidFilter"); }
    }

    public static JsonObject List(IEnumerable<JsonObject> resources, int total, int startIndex)
    {
        var array = new JsonArray(resources.Select(r => (JsonNode)r).ToArray());
        return new() { ["schemas"] = new JsonArray(ListSchema), ["totalResults"] = total,
            ["startIndex"] = startIndex, ["itemsPerPage"] = array.Count, ["Resources"] = array };
    }

    public static string Version(Guid revision) => $"W/\"{revision:N}\"";
    public static void CheckVersion(string? supplied, Guid revision)
    {
        if (supplied is not null && supplied != "*" && supplied != Version(revision))
            throw new ScimException(412, "The resource changed. Read it again before updating.");
    }

    [GeneratedRegex("^\\s*(id|externalId|userName|displayName)\\s+eq\\s+(\"(?:[^\"\\\\]|\\\\.)*\")\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EqualityFilter();
}
