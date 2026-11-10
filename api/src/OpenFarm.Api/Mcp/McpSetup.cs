using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.Mcp;

public static class McpSetup
{
    public const int MaxRequestBytes = 128 * 1024;

    public static IServiceCollection AddOpenFarmMcp(this IServiceCollection services)
    {
        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = "openfarm",
                    Version = typeof(McpSetup).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                };
                o.ServerInstructions = McpInstructions.Text;
                o.Filters.Request.CallToolFilters.Add(McpCall.Filter);
            })
            .WithHttpTransport(t =>
            {
                t.SessionMode = HttpServerSessionMode.Stateless;
                // Runs per request in stateless mode: list only the tools this credential may call now.
                t.ConfigureSessionOptions = async (http, options, _) =>
                {
                    var allowed = await CredentialAccess.EffectiveAsync(http);
                    var tools = new McpServerPrimitiveCollection<McpServerTool>();
                    foreach (var spec in McpTools.All.Where(s => s.Permissions.All(allowed.Contains))) tools.Add(spec.Tool);
                    options.ToolCollection = tools;
                };
            });
        return services;
    }
}
