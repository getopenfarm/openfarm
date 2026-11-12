using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using OpenFarm.Application.Auth;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Auth.OAuth;

namespace OpenFarm.Api.OAuth;

/// <summary>
/// The OAuth protocol routes (RFC 8414, 9728, 7591, 6749 + PKCE, 7009, 9207). Minimal API, not FastEndpoints:
/// they need RFC 6749 <c>{"error"}</c> bodies, form bodies with duplicate checks, and stay out of OpenAPI.
/// Every value in a response comes from <see cref="OAuthSettings"/>, never from the request's Host or headers.
/// </summary>
public static partial class OAuthProtocolEndpoints
{
    public const int MaxBodyBytes = 16 * 1024;
    public const int MaxQueryChars = 8192;
    public const string RegisterLimit = "oauth-register";
    public const string Limit = "oauth";

    /// <summary>Answered with 405 <c>{"error":"invalid_request"}</c> and <c>Allow: POST</c> for any other method.</summary>
    public static readonly HashSet<string> PostOnly = new(StringComparer.Ordinal) { "/oauth/register", "/oauth/token", "/oauth/revoke" };

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Per-address fixed windows from <c>OAuth:RegisterPermitLimit</c> (10) and <c>OAuth:PermitLimit</c> (60) a minute.</summary>
    public static IServiceCollection AddOAuthRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var registerLimit = configuration.GetValue("OAuth:RegisterPermitLimit", 10);
        var limit = configuration.GetValue("OAuth:PermitLimit", 60);
        return services.AddRateLimiter(o =>
        {
            // The socket address, never X-Forwarded-For. Behind a tunnel every client shares one bucket: fails safe.
            o.AddPolicy(RegisterLimit, c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "-",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = registerLimit, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy(Limit, c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "-",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) }));
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                if (context.HttpContext.Request.Path.StartsWithSegments("/oauth/authorize"))
                {
                    // A browser is on the other end: show OpenFarm's own page.
                    response.StatusCode = StatusCodes.Status302Found;
                    response.Headers.Location = "/connect/error?code=temporarily_unavailable";
                    return;
                }
                response.Headers.RetryAfter = "60";
                await response.WriteAsJsonAsync(new { error = "temporarily_unavailable" }, ct);
            };
        });
    }

    public static void MapOAuthProtocol(this WebApplication app)
    {
        app.MapGet("/.well-known/oauth-protected-resource/mcp", (OAuthSettings settings) => ResourceMetadata(settings)).ExcludeFromDescription();
        app.MapGet("/.well-known/oauth-protected-resource", (OAuthSettings settings) => ResourceMetadata(settings)).ExcludeFromDescription();
        app.MapGet("/.well-known/oauth-authorization-server", (OAuthSettings settings) => AuthorizationServerMetadata(settings)).ExcludeFromDescription();
        app.MapPost("/oauth/register", RegisterAsync).RequireRateLimiting(RegisterLimit).ExcludeFromDescription();
        app.MapGet("/oauth/authorize", AuthorizeAsync).RequireRateLimiting(Limit).ExcludeFromDescription();
        app.MapPost("/oauth/token", TokenAsync).RequireRateLimiting(Limit).ExcludeFromDescription();
        app.MapPost("/oauth/revoke", RevokeAsync).RequireRateLimiting(Limit).ExcludeFromDescription();
    }

    private static readonly string[] Scopes = [.. OAuthScopes.All.Select(s => s.Scope)];
    private static readonly string[] Header = ["header"];
    private static readonly string[] Code = ["code"];
    private static readonly string[] Query = ["query"];
    private static readonly string[] Grants = ["authorization_code", "refresh_token"];
    private static readonly string[] AuthMethods = ["none", "client_secret_basic", "client_secret_post"];
    private static readonly string[] S256 = ["S256"];

    private static IResult ResourceMetadata(OAuthSettings s) => !s.Enabled ? Disabled() : Results.Json(new
    {
        resource = s.Resource,
        authorization_servers = new[] { s.Issuer },
        scopes_supported = Scopes,
        bearer_methods_supported = Header,
        resource_name = "OpenFarm",
    }, Json);

    private static IResult AuthorizationServerMetadata(OAuthSettings s) => !s.Enabled ? Disabled() : Results.Json(new
    {
        issuer = s.Issuer,
        authorization_endpoint = s.Issuer + "/oauth/authorize",
        token_endpoint = s.Issuer + "/oauth/token",
        registration_endpoint = s.Issuer + "/oauth/register",
        revocation_endpoint = s.Issuer + "/oauth/revoke",
        response_types_supported = Code,
        response_modes_supported = Query,
        grant_types_supported = Grants,
        token_endpoint_auth_methods_supported = AuthMethods,
        revocation_endpoint_auth_methods_supported = AuthMethods,
        code_challenge_methods_supported = S256,
        authorization_response_iss_parameter_supported = true,
        scopes_supported = Scopes,
    }, Json);

    /// <summary>
    /// Validates the request and sends the browser to the consent page. Every failure stays on OpenFarm
    /// (<c>/connect/error</c>), never on the client's redirect URI, which may be the thing that is invalid.
    /// </summary>
    private static async Task<IResult> AuthorizeAsync(HttpContext http, OAuthSettings settings, OAuthService oauth)
    {
        if (!settings.Enabled) return ErrorPage("disabled");
        if (http.Request.QueryString.Value is { Length: > MaxQueryChars } || OAuthRules.ParseParameters(http.Request.Query) is not { } parameters)
            return ErrorPage("invalid_request");
        try
        {
            var request = await oauth.BeginAsync(parameters, http.RequestAborted);
            return Results.Redirect("/connect?request=" + Uri.EscapeDataString(request));
        }
        catch (OAuthException e)
        {
            return ErrorPage(e.Code);
        }
    }

    private static IResult ErrorPage(string code) => Results.Redirect("/connect/error?code=" + Uri.EscapeDataString(code));

    private static async Task<IResult> RegisterAsync(HttpContext http, OAuthSettings settings, OAuthService oauth, ILogger<OAuthService> logger)
    {
        if (!settings.Enabled) return Disabled();
        if (!IsMediaType(http, "application/json")) return Error("invalid_request");
        if (await ReadBodyAsync(http) is not { } body) return Error("invalid_request", StatusCodes.Status413PayloadTooLarge);
        return await RunAsync(http, logger, async () => Results.Json(await oauth.RegisterAsync(body, http.RequestAborted), Json, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> TokenAsync(HttpContext http, OAuthSettings settings, OAuthService oauth, ILogger<OAuthService> logger)
    {
        if (!settings.Enabled) return Disabled();
        if (await FormAsync(http) is not { } form) return FormError(http);
        return await RunAsync(http, logger, async () => Results.Json(await oauth.ExchangeAsync(form, Authorization(http), http.RequestAborted), Json));
    }

    private static async Task<IResult> RevokeAsync(HttpContext http, OAuthSettings settings, OAuthService oauth, ILogger<OAuthService> logger)
    {
        if (!settings.Enabled) return Disabled();
        if (await FormAsync(http) is not { } form) return FormError(http);
        return await RunAsync(http, logger, async () =>
        {
            await oauth.RevokeAsync(form, Authorization(http), http.RequestAborted);
            return Results.Ok();
        });
    }

    private static async Task<IResult> RunAsync(HttpContext http, ILogger logger, Func<Task<IResult>> run)
    {
        try
        {
            return await run();
        }
        catch (OAuthException e)
        {
            if (e.Status == StatusCodes.Status401Unauthorized && http.Request.Headers.Authorization.Count > 0)
                http.Response.Headers.WWWAuthenticate = "Basic realm=\"openfarm-oauth\"";
            return Error(e.Code, e.Status);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Library and database text can leak details: return the standard code, log the type only.
            LogFailure(logger, http.Request.Path, e.GetType().Name);
            return Error("server_error", StatusCodes.Status500InternalServerError);
        }
    }

    private static string? Authorization(HttpContext http) =>
        http.Request.Headers.Authorization.ToString() is { Length: > 0 } header ? header : null;

    /// <summary>A form body with no repeated or overlong parameter, or null (see <see cref="FormError"/>).</summary>
    private static async Task<Dictionary<string, string>?> FormAsync(HttpContext http)
    {
        if (!IsMediaType(http, "application/x-www-form-urlencoded")) return null;
        if (await ReadBodyAsync(http) is not { } body)
        {
            http.Items[nameof(FormAsync)] = StatusCodes.Status413PayloadTooLarge;
            return null;
        }
        return OAuthRules.ParseParameters(QueryHelpers.ParseQuery(body));
    }

    private static IResult FormError(HttpContext http) =>
        Error("invalid_request", http.Items[nameof(FormAsync)] as int? ?? StatusCodes.Status400BadRequest);

    private static bool IsMediaType(HttpContext http, string mediaType) =>
        MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var parsed)
        && string.Equals(parsed.MediaType.Value, mediaType, StringComparison.OrdinalIgnoreCase);

    /// <summary>The body as UTF-8, or null past <see cref="MaxBodyBytes"/> (also for chunked bodies with no Content-Length).</summary>
    private static async Task<string?> ReadBodyAsync(HttpContext http)
    {
        if (http.Request.ContentLength > MaxBodyBytes) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static IResult Error(string code, int status = StatusCodes.Status400BadRequest) => Results.Json(new { error = code }, Json, statusCode: status);

    private static IResult Disabled() => Results.Json(new { error = "OAuth is disabled" }, Json, statusCode: StatusCodes.Status404NotFound);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OAuth {Path} failed with {ExceptionType}")]
    private static partial void LogFailure(ILogger logger, string path, string exceptionType);
}
