using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.ExceptionHandling;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.Mcp;

/// <summary>
/// Wraps every tool call: rechecks the caller's current access (the per-request tool list only hides
/// tools), maps failures to <c>{"error":{"code","message"}}</c> results, and writes one log line.
/// </summary>
public static partial class McpCall
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            var name = request.Params?.Name ?? "";
            if (McpTools.Find(name) is not { } spec) return await next(request, ct);
            var http = request.Service<IHttpContextAccessor>().HttpContext!;
            var logger = request.Service<ILoggerFactory>().CreateLogger(typeof(McpCall));
            var started = Stopwatch.GetTimestamp();
            var outcome = "ok";
            try
            {
                var allowed = await CredentialAccess.EffectiveAsync(http);
                if (!spec.Permissions.All(allowed.Contains))
                    return Error(outcome = "FORBIDDEN", "This connection no longer has access to this tool.");
                var result = await next(request, ct);
                if (result.IsError is true) outcome = "error";
                return result;
            }
            catch (Exception e) when (e is not McpProtocolException && !(e is OperationCanceledException && ct.IsCancellationRequested))
            {
                var (code, message, extras) = Describe(e, spec.Tool.ProtocolTool.Annotations?.ReadOnlyHint is true);
                if (code == "INTERNAL_ERROR") LogFailure(logger, name, e.GetType().Name);
                outcome = code;
                return Error(code, message, extras);
            }
            finally
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var userId = http.User.FindFirst("sub")?.Value;
                    var channel = Channel(http);
                    LogCall(logger, name, userId, channel, outcome, elapsedMs);
                }
            }
        };

    private static (string Code, string Message, IDictionary<string, object?>? Extras) Describe(Exception e, bool read)
    {
        if (e is KeyNotFoundException) return ("NOT_FOUND", e.Message, null);
        if (e is FluentValidation.ValidationException invalid)
            return ("INVALID_INPUT", "Invalid input", new Dictionary<string, object?>
            {
                ["issues"] = invalid.Errors.Select(f => new { field = f.PropertyName, message = f.ErrorMessage }).ToArray(),
            });
        var problem = DomainExceptionHandler.ToProblem(e);
        return problem?.Status switch
        {
            404 => ("NOT_FOUND", problem.Title!, problem.Extensions),
            409 => ("CONFLICT", "This item changed since it was read. Read it again and check with the user before retrying.", problem.Extensions),
            400 or 422 => ("INVALID_INPUT", problem.Title!, problem.Extensions),
            _ => ("INTERNAL_ERROR", read
                ? "The read could not be completed. Try a narrower query."
                : "The change could not be completed. Read the item again before retrying, in case part of it was saved.", null),
        };
    }

    public static CallToolResult Error(string code, string message, IDictionary<string, object?>? extras = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        foreach (var (key, value) in extras ?? new Dictionary<string, object?>())
            error[key] = JsonSerializer.SerializeToNode(value, McpJsonUtilities.DefaultOptions);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = new JsonObject { ["error"] = error }.ToJsonString() }],
        };
    }

    private static string Channel(HttpContext http) =>
        CredentialClaims.Channel(http.User) is { } via ? $"{via.Kind}:{via.CredentialId}" : "unknown";

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP {Tool} by {UserId} via {Channel} → {Outcome} in {ElapsedMs} ms")]
    private static partial void LogCall(ILogger logger, string tool, string? userId, string channel, string outcome, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP {Tool} failed with {ExceptionType}")]
    private static partial void LogFailure(ILogger logger, string tool, string exceptionType);
}
