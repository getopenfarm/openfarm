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
        .. AccountTools.All,
        .. PrinterTools.All,
        .. PrintJobTools.All,
        .. ProductionRunTools.All,
        .. LibraryTools.All,
        .. FilamentTools.All,
        .. MaintenanceTools.All,
        .. WriteTools.All,
    ];

    private static readonly Dictionary<string, McpToolSpec> ByName = All.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public static McpToolSpec? Find(string name) => ByName.GetValueOrDefault(name);

    public const string PagingNote = "Pages reflect current data; repeat the query if items change while paging.";

    /// <summary>A read-only tool: never changes farm state.</summary>
    public static McpToolSpec Read(string name, string title, string description, Delegate method, params string[] permissions) =>
        new(McpServerTool.Create(method, new McpServerToolCreateOptions
        {
            Name = name,
            Title = title,
            Description = description,
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true,
        }), permissions);

    /// <summary>A tool that changes farm state. Every write states <c>destructive</c> explicitly.</summary>
    public static McpToolSpec Write(string name, string title, string description, bool destructive, bool idempotent,
        bool openWorld, Delegate method, params string[] permissions) =>
        new(McpServerTool.Create(method, new McpServerToolCreateOptions
        {
            Name = name,
            Title = title,
            Description = description,
            ReadOnly = false,
            Destructive = destructive,
            Idempotent = idempotent,
            OpenWorld = openWorld,
            UseStructuredContent = true,
        }), permissions);

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

    /// <summary>Throws the NOT_FOUND tool error when a lookup found nothing.</summary>
    public static T Found<T>(T? value, string what) where T : class =>
        value ?? throw new KeyNotFoundException($"{what} not found");

    public static int PageSize(int? requested) => requested is >= 1 and <= 100 ? requested.Value : 25;

    public static int Page(int? requested) => Math.Max(requested ?? 1, 1);
}
