using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenFarm.Infrastructure.Auth;

public static partial class ScimPatch
{
    public static JsonObject Apply(JsonObject original, JsonObject patch, bool group)
    {
        ScimProtocol.RequireSchema(patch, ScimProtocol.PatchSchema);
        if (ScimProtocol.Get(patch, "Operations") is not JsonArray operations || operations.Count is < 1 or > 100)
            throw new ScimException(400, "PATCH requires 1–100 operations.", "invalidValue");
        var result = (JsonObject)original.DeepClone();
        foreach (var node in operations)
        {
            if (node is not JsonObject operation) throw new ScimException(400, "Invalid PATCH operation.", "invalidValue");
            var op = ScimProtocol.Required(operation, "op").ToLowerInvariant();
            if (op is not ("add" or "replace" or "remove")) throw new ScimException(400, "Unsupported PATCH operation.", "invalidSyntax");
            var path = ScimProtocol.Text(operation, "path", 512);
            var value = ScimProtocol.Get(operation, "value");
            if (string.IsNullOrEmpty(path))
            {
                if (op == "remove" || value is not JsonObject values) throw new ScimException(400, "A path or attribute object is required.", "noTarget");
                foreach (var attribute in values) ApplyPath(result, op, attribute.Key, attribute.Value, group);
            }
            else ApplyPath(result, op, path, value, group);
        }
        return result;
    }

    private static void ApplyPath(JsonObject result, string op, string path, JsonNode? value, bool group)
    {
        var prefix = (group ? ScimProtocol.GroupSchema : ScimProtocol.UserSchema) + ":";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) path = path[prefix.Length..];
        var lower = path.ToLowerInvariant();
        if (lower == "schemas" && op != "remove")
        {
            var schema = group ? ScimProtocol.GroupSchema : ScimProtocol.UserSchema;
            var wrapper = new JsonObject { ["schemas"] = value?.DeepClone() };
            ScimProtocol.RequireSchema(wrapper, schema);
            return;
        }
        if (group && lower.StartsWith("members", StringComparison.Ordinal))
        {
            var members = ScimProtocol.Get(result, "members") as JsonArray ?? [];
            if (lower != "members")
            {
                var match = MemberPath().Match(path);
                if (!match.Success || op != "remove") throw new ScimException(400, "Unsupported member path.", "invalidPath");
                var id = match.Groups[1].Value;
                if (!Guid.TryParse(id, out var parsed)) throw new ScimException(400, "Invalid member identifier.", "invalidPath");
                foreach (var member in members.OfType<JsonObject>().Where(m => Guid.TryParse(ScimProtocol.Text(m, "value"), out var candidate) && candidate == parsed).ToArray()) members.Remove(member);
            }
            else if (op == "remove") members.Clear();
            else
            {
                JsonArray incoming = value switch
                {
                    JsonArray array => array,
                    JsonObject single => new JsonArray(single.DeepClone()),
                    _ => throw new ScimException(400, "members must contain user references.", "invalidValue"),
                };
                if (op == "replace") members.Clear();
                foreach (var member in incoming) members.Add(member?.DeepClone());
            }
            ScimProtocol.Set(result, "members", members);
            return;
        }
        if (!group && (lower.StartsWith("emails[", StringComparison.Ordinal)))
        {
            if (!EmailPath().IsMatch(path) || op == "remove") throw new ScimException(400, "Unsupported email path.", "invalidPath");
            ScimProtocol.Set(result, "emails", new JsonArray(new JsonObject { ["value"] = value?.DeepClone(), ["primary"] = true, ["type"] = "work" }));
            return;
        }
        if (!group && lower.StartsWith("name.", StringComparison.Ordinal))
        {
            var sub = lower[5..];
            if (sub is not ("givenname" or "familyname" or "formatted")) throw new ScimException(400, "Unsupported name attribute.", "invalidPath");
            var name = ScimProtocol.Get(result, "name") as JsonObject ?? [];
            ScimProtocol.Set(name, sub, op == "remove" ? null : value);
            ScimProtocol.Set(result, "name", name);
            return;
        }
        if (!group && lower == "name" && op == "add" && value is JsonObject addedName)
        {
            var name = ScimProtocol.Get(result, "name") as JsonObject ?? [];
            foreach (var attribute in addedName) ScimProtocol.Set(name, attribute.Key, attribute.Value);
            ScimProtocol.Set(result, "name", name);
            return;
        }
        if (!group && lower == "emails" && op == "add")
        {
            if (value is not JsonArray incoming) throw new ScimException(400, "emails must be an array.", "invalidValue");
            var emails = ScimProtocol.Get(result, "emails") as JsonArray ?? [];
            foreach (var item in incoming)
            {
                if (item is not JsonObject email) throw new ScimException(400, "Invalid email entry.", "invalidValue");
                var address = ScimProtocol.Required(email, "value");
                if (!emails.OfType<JsonObject>().Any(e => string.Equals(ScimProtocol.Text(e, "value"), address, StringComparison.OrdinalIgnoreCase)))
                    emails.Add(email.DeepClone());
            }
            ScimProtocol.Set(result, "emails", emails);
            return;
        }
        var allowed = group ? lower is "displayname" or "externalid"
            : lower is "username" or "displayname" or "active" or "name" or "emails" or "externalid";
        if (!allowed) throw new ScimException(400, "The attribute is read-only or unsupported.", "mutability");
        if (op == "remove" && lower is not ("displayname" or "name")) throw new ScimException(400, "A required or immutable attribute cannot be removed.", "mutability");
        ScimProtocol.Set(result, path, op == "remove" ? null : value);
    }

    [GeneratedRegex("^members\\s*\\[\\s*value\\s+eq\\s+\"([0-9a-f-]{36})\"\\s*\\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MemberPath();

    [GeneratedRegex("^emails\\s*\\[\\s*(?:type\\s+eq\\s+\"work\"|primary\\s+eq\\s+true)\\s*\\]\\.value$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EmailPath();
}
