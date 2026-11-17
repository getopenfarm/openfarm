using System.Text.Json.Nodes;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

public static class ScimDiscovery
{
    public static JsonObject Configuration() => new()
    {
        ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig"),
        ["patch"] = new JsonObject { ["supported"] = true },
        // Authentik also uses maxOperations to size PATCH membership batches.
        ["bulk"] = new JsonObject { ["supported"] = false, ["maxOperations"] = 100, ["maxPayloadSize"] = 262144 },
        ["filter"] = new JsonObject { ["supported"] = true, ["maxResults"] = 200 },
        ["changePassword"] = new JsonObject { ["supported"] = false },
        ["sort"] = new JsonObject { ["supported"] = false },
        ["etag"] = new JsonObject { ["supported"] = true },
        ["authenticationSchemes"] = new JsonArray(new JsonObject
        {
            ["type"] = "oauthbearertoken",
            ["name"] = "Provisioning bearer token",
            ["primary"] = true,
            ["description"] = "A dedicated deployment-configured provisioning credential.",
            ["specUri"] = "https://www.rfc-editor.org/rfc/rfc6750",
        }),
    };

    public static JsonObject ResourceTypes(string origin) => ScimProtocol.List(
        [ResourceType("User", origin)!, ResourceType("Group", origin)!], 2, 1);

    public static JsonObject? ResourceType(string id, string origin) => id is "User" or "Group" ? new()
    {
        ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ResourceType"),
        ["id"] = id,
        ["name"] = id,
        ["endpoint"] = id == "User" ? "/Users" : "/Groups",
        ["schema"] = id == "User" ? ScimProtocol.UserSchema : ScimProtocol.GroupSchema,
        ["schemaExtensions"] = new JsonArray(),
        ["meta"] = new JsonObject { ["resourceType"] = "ResourceType", ["location"] = $"{origin}/scim/v2/ResourceTypes/{id}" },
    } : null;

    public static JsonObject Schemas() => ScimProtocol.List([Schema(ScimProtocol.UserSchema)!, Schema(ScimProtocol.GroupSchema)!], 2, 1);

    public static JsonObject? Schema(string id)
    {
        if (id != ScimProtocol.UserSchema && id != ScimProtocol.GroupSchema) return null;
        var user = id == ScimProtocol.UserSchema;
        var attributes = new JsonArray(Attribute("id", "string", true, "readOnly", uniqueness: "server"),
            Attribute("externalId", "string", user, "immutable"), Attribute("displayName", "string", !user));
        if (user)
        {
            attributes.Add(Attribute("userName", "string", true, uniqueness: "server"));
            attributes.Add(Attribute("active", "boolean"));
            attributes.Add(Complex("name", false, false, "readWrite", Attribute("formatted", "string"), Attribute("givenName", "string"), Attribute("familyName", "string")));
            attributes.Add(Complex("emails", true, true, "readWrite", Attribute("value", "string", true), Attribute("type", "string"), Attribute("primary", "boolean")));
            attributes.Add(Complex("groups", true, false, "readOnly", Attribute("value", "string", true, "readOnly"), Attribute("display", "string", false, "readOnly"),
                Attribute("$ref", "reference", false, "readOnly"), Attribute("type", "string", false, "readOnly")));
        }
        else attributes.Add(Complex("members", true, false, "readWrite", Attribute("value", "string", true), Attribute("$ref", "reference"), Attribute("display", "string", false, "readOnly")));
        return new()
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:Schema"),
            ["id"] = id,
            ["name"] = user ? "User" : "Group",
            ["description"] = user ? "Provisioned OpenFarm user" : "External provisioning group",
            ["attributes"] = attributes,
        };
    }

    private static JsonObject Attribute(string name, string type, bool required = false, string mutability = "readWrite", string uniqueness = "none")
    {
        var result = new JsonObject
        {
            ["name"] = name,
            ["type"] = type,
            ["multiValued"] = false,
            ["required"] = required,
            ["caseExact"] = name == "externalId",
            ["mutability"] = mutability,
            ["returned"] = name == "id" ? "always" : "default",
            ["uniqueness"] = uniqueness
        };
        if (type == "reference") result["referenceTypes"] = new JsonArray("User", "Group");
        return result;
    }

    private static JsonObject Complex(string name, bool multiple, bool required, string mutability, params JsonObject[] attributes)
    {
        var result = Attribute(name, "complex", required, mutability);
        result["multiValued"] = multiple;
        result["subAttributes"] = new JsonArray(attributes.Select(a => (JsonNode)a).ToArray());
        return result;
    }
}
