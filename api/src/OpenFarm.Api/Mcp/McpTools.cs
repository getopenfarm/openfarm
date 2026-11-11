using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Mcp.Tools;
using OpenFarm.Api.Processors;

namespace OpenFarm.Api.Mcp;

/// <summary>An MCP tool and the permissions a caller needs to see and call it.</summary>
public sealed record McpToolSpec(McpServerTool Tool, IReadOnlyList<string> Permissions)
{
    public string Name => Tool.ProtocolTool.Name;
}

public static class McpTools
{
    public static readonly IReadOnlyList<McpToolSpec> All =
    [
        .. OverviewTools.All,
        .. PrinterTools.All,
        .. QueueTools.All,
        .. ProductionTools.All,
        .. LibraryTools.All,
        .. PricingTools.All,
        .. MaintenanceTools.All,
        .. FilamentTools.All,
    ];

    private static readonly Dictionary<string, McpToolSpec> ByName = All.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public static McpToolSpec? Find(string name) => ByName.GetValueOrDefault(name);

    public const string PagingNote = "Pages reflect current data; repeat the query if items change while paging.";
    public const string PageArg = "1-based page, default 1";
    public const string PageSizeArg = "1–100, default 25";

    /// <summary>A read-only tool: never changes farm state.</summary>
    public static McpToolSpec Read(string name, string title, string description, Delegate method, params string[] permissions) =>
        new(McpSchemas.Portable(McpServerTool.Create(method, new McpServerToolCreateOptions
        {
            Name = name,
            Title = title,
            Description = description,
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true,
        })), permissions);

    /// <summary>A tool that changes farm state. Every write states <c>destructive</c> explicitly.</summary>
    public static McpToolSpec Write(string name, string title, string description, bool destructive, bool idempotent,
        bool openWorld, Delegate method, params string[] permissions) =>
        new(McpSchemas.Portable(McpServerTool.Create(method, new McpServerToolCreateOptions
        {
            Name = name,
            Title = title,
            Description = description,
            ReadOnly = false,
            Destructive = destructive,
            Idempotent = idempotent,
            OpenWorld = openWorld,
            UseStructuredContent = true,
        })), permissions);

    /// <summary>
    /// Runs a write exactly as its REST endpoint does: the endpoint's request DTO and validator, then
    /// the same handler, then the same activity row and live events (<see cref="ActivityRecorder"/>).
    /// </summary>
    public static async Task<TResponse> WriteAsync<TRequest, TResponse>(this RequestContext<CallToolRequestParams> request,
        TRequest dto, Func<Task<TResponse>> run, CancellationToken ct) where TRequest : IActivityLoggable
    {
        if (request.Services!.GetService<IValidator<TRequest>>() is { } validator)
            await validator.ValidateAndThrowAsync(dto, ct);
        var response = await run();
        await ActivityRecorder.RecordAsync(request.Service<IHttpContextAccessor>().HttpContext!, dto, response, ct);
        return response;
    }

    public static T Service<T>(this RequestContext<CallToolRequestParams> request) where T : notnull =>
        request.Services!.GetRequiredService<T>();

    public static HttpContext Http(this RequestContext<CallToolRequestParams> request) =>
        request.Service<IHttpContextAccessor>().HttpContext!;

    public static Guid UserId(this RequestContext<CallToolRequestParams> request) =>
        Guid.Parse(request.Http().User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!.Value);

    /// <summary>What this connection may use now: the account's permissions narrowed to the credential.</summary>
    public static Task<IReadOnlySet<string>> AllowedAsync(this RequestContext<CallToolRequestParams> request) =>
        Security.CredentialAccess.EffectiveAsync(request.Http());

    /// <summary>Throws the FORBIDDEN tool error unless the connection holds every permission; for checks that depend on an argument.</summary>
    public static async Task RequireAsync(this RequestContext<CallToolRequestParams> request, params string[] permissions)
    {
        var allowed = await request.AllowedAsync();
        if (permissions.FirstOrDefault(p => !allowed.Contains(p)) is { } missing)
            throw new UnauthorizedAccessException($"This connection lacks the {missing} permission this call needs.");
    }

    /// <summary>Throws the NOT_FOUND tool error when a lookup found nothing; <paramref name="hint"/> names the tool that finds valid ids.</summary>
    public static T Found<T>(T? value, string what, string? hint = null) where T : class =>
        value ?? throw new KeyNotFoundException(hint is null ? $"{what} not found." : $"{what} not found. {hint}");

    public static int PageSize(int? requested) => Math.Clamp(requested ?? 25, 1, 100);

    public static int Page(int? requested) => Math.Clamp(requested ?? 1, 1, 100_000);
}
