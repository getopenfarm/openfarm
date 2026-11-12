using Microsoft.AspNetCore.Hosting;
using OpenFarm.Api.IntegrationTests.Production;

namespace OpenFarm.Api.IntegrationTests.Mcp;

/// <summary>Tool and transport tests explicitly opt in; the ordinary installation stays default-off.</summary>
public sealed class McpTestFactory : ProductionTestFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("MCP_MODE", "AccessTokens");
    }
}
