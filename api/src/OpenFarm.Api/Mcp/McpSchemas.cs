using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace OpenFarm.Api.Mcp;

/// <summary>
/// Makes generated tool schemas portable: .NET writes a nullable field as <c>"type": ["string", "null"]</c>, which some
/// clients (Gemini's OpenAPI subset; MCP Inspector warns) reject, so it becomes <c>anyOf</c> with one type per branch.
/// Local <c>$ref</c>s are inlined first, since the rewrite moves the properties they point at.
/// Results leave null fields out (fewer tokens), so an output schema requires only fields .NET can never omit: numbers,
/// booleans, ids and dates. A non-nullable string, list or object can still be null at runtime, and a client validating
/// the result would then reject the whole call.
/// </summary>
public static class McpSchemas
{
    private const int MaxDepth = 64;

    public static McpServerTool Portable(McpServerTool tool)
    {
        tool.ProtocolTool.InputSchema = Portable(tool.ProtocolTool.InputSchema, output: false);
        if (tool.ProtocolTool.OutputSchema is { } schema) tool.ProtocolTool.OutputSchema = Portable(schema, output: true);
        return tool;
    }

    public static JsonElement Portable(JsonElement schema, bool output)
    {
        var root = JsonNode.Parse(schema.GetRawText())!;
        return JsonSerializer.SerializeToElement(Nullables(Inline(root, root, 0), output));
    }

    private static JsonNode? Inline(JsonNode? node, JsonNode root, int depth)
    {
        if (depth > MaxDepth) throw new InvalidOperationException("Tool schema nests too deeply; a recursive type cannot be inlined.");
        switch (node)
        {
            case JsonObject o when o["$ref"] is JsonValue r && r.GetValue<string>() is var pointer && pointer.StartsWith('#'):
                var target = Resolve(root, pointer) as JsonObject ?? throw new InvalidOperationException($"Tool schema $ref {pointer} does not resolve.");
                var merged = (JsonObject)target.DeepClone();
                // The exporter can write an empty sibling such as "items": {} next to a $ref; it must not replace the target's.
                foreach (var (key, value) in o)
                    if (key != "$ref" && (!merged.ContainsKey(key) || key is "description" or "title")) merged[key] = value?.DeepClone();
                return Inline(merged, root, depth + 1);
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (key, value) in o) copy[key] = Inline(value, root, depth + 1);
                return copy;
            case JsonArray a:
                return new JsonArray([.. a.Select(item => Inline(item, root, depth + 1))]);
            default:
                return node?.DeepClone();
        }
    }

    private static JsonNode? Resolve(JsonNode root, string pointer) =>
        pointer.Length <= 2 ? root : pointer[2..].Split('/').Select(s => s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
            .Aggregate((JsonNode?)root, (node, segment) => node switch
            {
                JsonObject o => o[segment],
                JsonArray a when int.TryParse(segment, out var i) && i < a.Count => a[i],
                _ => null,
            });

    private static JsonNode? Nullables(JsonNode? node, bool output)
    {
        if (node is JsonArray a) return new JsonArray([.. a.Select(item => Nullables(item, output))]);
        if (node is not JsonObject o) return node?.DeepClone();
        var result = new JsonObject();
        foreach (var (key, value) in o) result[key] = Nullables(value, output);
        if (result["properties"] is JsonObject properties && result["required"] is JsonArray required)
            result["required"] = new JsonArray([.. required.Where(name => name is not null && properties[name.GetValue<string>()] is var property
                    && !AllowsNull(property) && (!output || NeverOmitted(property)))
                .Select(name => name!.DeepClone())]);
        if (result["type"] is not JsonArray types || !types.Any(t => t?.GetValue<string>() == "null")) return result;

        var others = types.Select(t => t!.GetValue<string>()).Where(t => t != "null").ToList();
        if (others.Count == 0) return result;
        // The outer node keeps what describes the field; the typed branch takes every constraint.
        var branch = new JsonObject { ["type"] = others.Count == 1 ? others[0] : new JsonArray([.. others.Select(t => (JsonNode)t)]) };
        foreach (var key in result.Select(p => p.Key).Where(k => k is not ("type" or "description" or "title" or "default")).ToList())
        {
            var moved = result[key];
            result.Remove(key);
            branch[key] = moved;
        }
        if (branch["enum"] is JsonArray values)
            branch["enum"] = new JsonArray([.. values.Where(v => v is not null).Select(v => v!.DeepClone())]);
        result.Remove("type");
        result["anyOf"] = new JsonArray(branch, new JsonObject { ["type"] = "null" });
        return result;
    }

    /// <summary>A value type's schema: a number, a boolean, or a string formatted as a Guid, DateTimeOffset or DateOnly.</summary>
    private static bool NeverOmitted(JsonNode? schema) =>
        schema is JsonObject o && o["type"] is JsonValue t && t.GetValue<string>() switch
        {
            "integer" or "number" or "boolean" => true,
            "string" => o["format"] is JsonValue f && f.GetValue<string>() is "uuid" or "date-time" or "date",
            _ => false,
        };

    /// <summary>Whether a rewritten property schema accepts null: an <c>anyOf</c> with a null branch.</summary>
    private static bool AllowsNull(JsonNode? schema) =>
        schema is JsonObject o && o["anyOf"] is JsonArray branches
        && branches.Any(b => b is JsonObject branch && branch["type"] is JsonValue t && t.GetValue<string>() == "null");
}
