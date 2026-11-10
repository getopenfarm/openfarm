using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Maintenance;
using OpenFarm.Api.Endpoints.Notifications;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Api.Endpoints.Reports;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Api.Mcp;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.IntegrationTests.Mcp;

public sealed class McpReadToolsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    /// <summary>The REST endpoint each tool mirrors; its permissions must match the tool's.</summary>
    private static readonly Dictionary<string, Type> Mirrors = new()
    {
        ["list_printers"] = typeof(ListFleetEndpoint),
        ["get_printer"] = typeof(GetPrinterDetailEndpoint),
        ["list_print_jobs"] = typeof(ListQueueJobsEndpoint),
        ["get_print_job"] = typeof(GetPrintJobEndpoint),
        ["list_notifications"] = typeof(ListNotificationsEndpoint),
        ["list_production_runs"] = typeof(ListProductionRunsEndpoint),
        ["get_production_run"] = typeof(GetProductionRunEndpoint),
        ["list_parts"] = typeof(ListPartsEndpoint),
        ["get_part"] = typeof(GetPartEndpoint),
        ["list_assemblies"] = typeof(ListAssembliesEndpoint),
        ["get_assembly"] = typeof(GetAssemblyEndpoint),
        ["list_filaments"] = typeof(ListFilamentsEndpoint),
        ["list_filament_shortages"] = typeof(ListFilamentShortagesEndpoint),
        ["list_spools"] = typeof(ListSpoolsEndpoint),
        ["list_maintenance"] = typeof(ListMaintenanceEnrollmentsEndpoint),
        ["get_farm_stats"] = typeof(StatsEndpoint),
    };

    [Fact]
    public void EveryToolNeedsExactlyItsMirroredEndpointsPermissions()
    {
        McpTools.All.Select(t => t.Name).Should().BeEquivalentTo(Mirrors.Keys.Append("whoami"));
        McpTools.Find("whoami")!.Permissions.Should().BeEmpty();
        foreach (var (tool, endpoint) in Mirrors)
        {
            var attribute = endpoint.GetCustomAttribute<PermissionAttribute>()!;
            // PermissionAuthorizationHandler adds parts:list or assemblies:view to a library:* endpoint by its route; expect that here when one is mirrored.
            attribute.Permission.Should().NotStartWith("library:", tool);
            McpTools.Find(tool)!.Permissions.Should().BeEquivalentTo(attribute.AdditionalPermissions.Prepend(attribute.Permission), tool);
        }
    }

    [Fact]
    public async Task EveryReadPermissionListsExactlyTheReadTools()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(McpTools.All.Select(t => t.Name)).And.HaveCount(17);
        tools.Should().OnlyContain(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true);
    }

    [Theory]
    [InlineData("list_printers")]
    [InlineData("list_print_jobs")]
    [InlineData("list_notifications")]
    [InlineData("list_production_runs")]
    [InlineData("list_parts")]
    [InlineData("list_assemblies")]
    [InlineData("list_filaments")]
    [InlineData("list_filament_shortages")]
    [InlineData("list_spools")]
    [InlineData("list_maintenance")]
    public async Task ListToolsReturnTheirResponse(string tool)
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync(tool);

        result.IsError.Should().NotBe(true, Text(result));
        result.StructuredContent!.Value.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task ListToolsTakeTheEndpointsFilters()
    {
        await using var client = await ConnectAsync();

        var external = await client.CallToolAsync("list_print_jobs", new Dictionary<string, object?> { ["view"] = "external", ["status"] = "Completed", ["page"] = 2, ["pageSize"] = 500 });
        external.IsError.Should().NotBe(true, Text(external));
        var spools = await client.CallToolAsync("list_spools", new Dictionary<string, object?> { ["state"] = "using", ["search"] = "pla" });
        spools.IsError.Should().NotBe(true, Text(spools));

        ErrorCode(await client.CallToolAsync("list_print_jobs", new Dictionary<string, object?> { ["status"] = "Nope" })).Should().Be("INVALID_INPUT");
        ErrorCode(await client.CallToolAsync("list_production_runs", new Dictionary<string, object?> { ["status"] = "Nope" })).Should().Be("INVALID_INPUT");
        ErrorCode(await client.CallToolAsync("list_parts", new Dictionary<string, object?> { ["folder"] = Guid.NewGuid() })).Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("get_print_job", "printJobId")]
    [InlineData("get_production_run", "productionRunId")]
    [InlineData("get_part", "partId")]
    [InlineData("get_assembly", "assemblyId")]
    public async Task GetToolsReportAMissingItemAsNotFound(string tool, string idArgument)
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync(tool, new Dictionary<string, object?> { [idArgument] = Guid.NewGuid() });

        ErrorCode(result).Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task FarmStatsValidatesTheRange()
    {
        await using var client = await ConnectAsync();
        var to = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        var stats = await client.CallToolAsync("get_farm_stats", new Dictionary<string, object?> { ["from"] = to.AddDays(-30), ["to"] = to, ["timeZone"] = "UTC" });
        stats.IsError.Should().NotBe(true, Text(stats));
        stats.StructuredContent!.Value.ValueKind.Should().Be(JsonValueKind.Object);

        var tooLong = await client.CallToolAsync("get_farm_stats", new Dictionary<string, object?> { ["from"] = to.AddDays(-400), ["to"] = to, ["timeZone"] = "UTC" });
        ErrorCode(tooLong).Should().Be("INVALID_INPUT");
        var backwards = await client.CallToolAsync("get_farm_stats", new Dictionary<string, object?> { ["from"] = to, ["to"] = to.AddDays(-1), ["timeZone"] = "Not/AZone" });
        ErrorCode(backwards).Should().Be("INVALID_INPUT");
    }

    private async Task<McpClient> ConnectAsync()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        var token = await McpTestClient.TokenAsync(browser, [.. McpTools.All.SelectMany(t => t.Permissions).Distinct()]);
        return await McpTestClient.ConnectAsync(factory, token.Credential);
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private static string? ErrorCode(CallToolResult result)
    {
        result.IsError.Should().BeTrue(Text(result));
        return JsonDocument.Parse(Text(result)).RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}
