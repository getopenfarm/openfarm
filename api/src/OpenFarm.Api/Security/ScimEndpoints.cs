using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Hosting;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

/// <summary>SCIM 2.0 protocol routes use their own credential, errors and media type.</summary>
public static class ScimEndpoints
{
    private const int MaxBody = 262144;

    public static void AddScimRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("Scim", http =>
            {
                var configuration = http.RequestServices.GetRequiredService<AuthenticationConfiguration>();
                var limit = configuration.Values.ScimRequestsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter($"{configuration.Revision}:{limit}", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
            var previous = options.OnRejected;
            options.OnRejected = async (context, ct) =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/scim"))
                    await ErrorAsync(context.HttpContext, 429, "Too many provisioning requests.");
                else if (previous is not null) await previous(context, ct);
            };
        });
    }

    public static async Task<bool> PrepareAsync(HttpContext http, ProvisioningSettings settings)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        if (!settings.ScimEnabled) { await ErrorAsync(http, 404, "SCIM is disabled."); return false; }
        if (!http.Request.Path.StartsWithSegments("/scim/v2")) { await ErrorAsync(http, 404, "SCIM resource not found."); return false; }
        if (http.Request.ContentLength > MaxBody) { await ErrorAsync(http, 413, "Request body is too large.", "tooMany"); return false; }
        var size = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false }) size.MaxRequestBodySize = MaxBody;
        return true;
    }

    public static void MapScim(this WebApplication app)
    {
        var routes = app.MapGroup("/scim/v2").RequireAuthorization("Scim").RequireRateLimiting("Scim").ExcludeFromDescription();
        routes.MapGet("/ServiceProviderConfig", () => Json(ScimDiscovery.Configuration()));
        routes.MapGet("/ResourceTypes", (PublicOrigin origin) => Json(ScimDiscovery.ResourceTypes(origin.Value!)));
        routes.MapGet("/ResourceTypes/{id}", (string id, PublicOrigin origin) =>
            ScimDiscovery.ResourceType(id, origin.Value!) is { } resource ? Json(resource) : JsonError(404, "Resource type not found."));
        routes.MapGet("/Schemas", () => Json(ScimDiscovery.Schemas()));
        routes.MapGet("/Schemas/{id}", (string id) => ScimDiscovery.Schema(id) is { } schema ? Json(schema) : JsonError(404, "Schema not found."));
        foreach (var group in new[] { false, true })
        {
            var resource = group ? "/Groups" : "/Users";
            routes.MapGet(resource, (HttpContext http, ScimService service, PublicOrigin origin) => HandleAsync(http, service, origin, group, null));
            routes.MapPost(resource, (HttpContext http, ScimService service, PublicOrigin origin) => HandleAsync(http, service, origin, group, null));
            routes.MapMethods(resource + "/{id}", ["GET", "PUT", "PATCH", "DELETE"],
                (HttpContext http, ScimService service, PublicOrigin origin, string id) => HandleAsync(http, service, origin, group, id));
        }
        // Unsupported protocol routes never fall through to the SPA or return non-SCIM error bodies.
        routes.Map("/{**path}", () => JsonError(404, "SCIM resource not found."));
    }

    private static async Task<IResult> HandleAsync(HttpContext http, ScimService service, PublicOrigin origin, bool group, string? rawId)
    {
        try
        {
            _ = Selection(http);
            Guid? id = rawId is null ? null : Guid.TryParse(rawId, out var parsed) ? parsed : throw new ScimException(404, "Resource not found.");
            JsonObject? result;
            var status = 200;
            if (HttpMethods.IsGet(http.Request.Method))
            {
                if (id is not null) result = await service.ReadAsync(group, id.Value, origin.Value!, http.RequestAborted);
                else
                {
                    if (http.Request.Query.ContainsKey("sortBy") || http.Request.Query.ContainsKey("sortOrder"))
                        throw new ScimException(400, "Sorting is not supported.", "invalidValue");
                    var start = Integer(http, "startIndex", 1, 1);
                    var count = Math.Min(Integer(http, "count", 100, 0), 200);
                    result = await service.ListAsync(group, http.Request.Query["filter"], start, count, origin.Value!, http.RequestAborted);
                }
            }
            else
            {
                var body = HttpMethods.IsDelete(http.Request.Method) ? null : await BodyAsync(http);
                result = await service.WriteAsync(group, http.Request.Method, id, body,
                    http.Request.Headers.IfMatch.Count == 0 ? null : http.Request.Headers.IfMatch.ToString(), origin.Value!, http.RequestAborted);
                status = HttpMethods.IsPost(http.Request.Method) ? 201 : result is null ? 204 : 200;
            }
            if (result is null) return Results.NoContent();
            if (result["meta"] is JsonObject meta)
            {
                http.Response.Headers.ETag = meta["version"]?.GetValue<string>();
                if (status == 201) http.Response.Headers.Location = meta["location"]?.GetValue<string>();
            }
            Project(result, http);
            return Json(result, status);
        }
        catch (ScimException e) { return JsonError(e.Status, e.Message, e.ScimType); }
        catch (ProvisioningConflictException) { return JsonError(409, "The identity, username or email is already in use.", "uniqueness"); }
        catch (ArgumentException) { return JsonError(400, "Invalid provisioning attributes.", "invalidValue"); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return JsonError(409, "The resource conflicts with an existing account or group.", "uniqueness"); }
        catch (JsonException) { return JsonError(400, "Malformed JSON body.", "invalidSyntax"); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { return JsonError(413, "Request body is too large.", "tooMany"); }
    }

    private static async Task<JsonObject> BodyAsync(HttpContext http)
    {
        var contentType = http.Request.ContentType?.Split(';')[0].Trim();
        if (!string.Equals(contentType, ScimProtocol.MediaType, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new ScimException(415, "Use application/scim+json or application/json.");
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await http.Request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxBody + 1 - (int)memory.Length)), http.RequestAborted)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > MaxBody) throw new ScimException(413, "Request body is too large.", "tooMany");
        }
        using var document = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        ValidateNames(document.RootElement);
        return JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject ?? throw new ScimException(400, "The body must be an object.", "invalidSyntax");
    }

    private static void ValidateNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ScimException(400, "Duplicate attribute names are not allowed.", "invalidSyntax");
                ValidateNames(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) ValidateNames(item);
    }

    private static int Integer(HttpContext http, string key, int fallback, int minimum) => !http.Request.Query.ContainsKey(key) ? fallback
        : int.TryParse(http.Request.Query[key], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= minimum
            ? value : throw new ScimException(400, $"Invalid {key}.", "invalidValue");

    private static void Project(JsonObject document, HttpContext http)
    {
        var (include, exclude, selected) = Selection(http);
        var resources = document["Resources"] is JsonArray array ? array.OfType<JsonObject>() : [document];
        foreach (var resource in resources)
            foreach (var key in resource.Select(p => p.Key).ToArray())
                if (key is not ("id" or "schemas") && ((include.Length > 0 && !selected.Contains(key)) || (exclude.Length > 0 && selected.Contains(key)))) resource.Remove(key);
    }

    private static (string Include, string Exclude, HashSet<string> Selected) Selection(HttpContext http)
    {
        var include = http.Request.Query["attributes"].ToString();
        var exclude = http.Request.Query["excludedAttributes"].ToString();
        if (include.Length > 1024 || exclude.Length > 1024 || (include.Length > 0 && exclude.Length > 0))
            throw new ScimException(400, "Invalid attribute selection.", "invalidValue");
        var selected = (include.Length > 0 ? include : exclude).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Any(s => s.Contains('.'))) throw new ScimException(400, "Select top-level attributes.", "invalidValue");
        return (include, exclude, selected);
    }

    public static Task ErrorAsync(HttpContext http, int status, string detail, string? type = null) => JsonError(status, detail, type).ExecuteAsync(http);
    private static IResult JsonError(int status, string detail, string? type = null)
    {
        var body = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimProtocol.ErrorSchema),
            ["status"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["detail"] = detail
        };
        if (type is not null) body["scimType"] = type;
        return Json(body, status);
    }
    private static IResult Json(JsonNode node, int status = 200) => Results.Json(node, contentType: ScimProtocol.MediaType, statusCode: status);
}
