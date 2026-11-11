using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Library;
using OpenFarm.Api.Endpoints.Maintenance;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Api.Endpoints.Reports;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Api.Mcp;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.IntegrationTests.Mcp;

public sealed class McpReadToolsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    /// <summary>
    /// The REST endpoints each tool does the work of. A tool needs exactly the union of their permissions, so MCP never reaches
    /// more than REST. A <c>library:*</c> endpoint also needs parts:list or assemblies:view by its route; those tools check it per call.
    /// </summary>
    private static readonly Dictionary<string, Type[]> Mirrors = new()
    {
        ["list_printers"] = [typeof(ListFleetEndpoint)],
        ["get_printer"] = [typeof(GetPrinterDetailEndpoint)],
        ["mark_printers_ready"] = [typeof(MarkPrinterReadyEndpoint)],
        ["list_print_jobs"] = [typeof(ListQueueJobsEndpoint)],
        ["get_print_job"] = [typeof(GetPrintJobEndpoint), typeof(GetJobMatchingDiagnosticsEndpoint)],
        ["reorder_print_job"] = [typeof(ReorderPrintJobEndpoint)],
        ["pin_print_job"] = [typeof(PinPrintJobEndpoint), typeof(UnpinPrintJobEndpoint)],
        ["requeue_print_job"] = [typeof(RequeuePrintJobEndpoint)],
        ["pause_print_job"] = [typeof(PausePrintJobEndpoint)],
        ["cancel_print_job"] = [typeof(CancelPrintJobEndpoint)],
        ["plan_production"] = [typeof(PreviewProductionPlanEndpoint)],
        ["queue_production"] = [typeof(SubmitProductionDemandEndpoint), typeof(CreateProductionRunEndpoint)],
        ["list_production_runs"] = [typeof(ListProductionRunsEndpoint)],
        ["get_production_run"] = [typeof(GetProductionRunEndpoint)],
        ["search_library"] = [typeof(ListLibraryContentsEndpoint)],
        ["get_part"] = [typeof(GetPartEndpoint)],
        ["get_assembly"] = [typeof(GetAssemblyEndpoint), typeof(ExplodeAssemblyEndpoint)],
        ["create_library_folder"] = [typeof(CreateFolderEndpoint)],
        ["rename_library_folder"] = [typeof(RenameFolderEndpoint)],
        ["move_library_items"] = [typeof(MoveFolderEndpoint), typeof(MoveLibraryItemsEndpoint)],
        ["update_parts"] = [typeof(EditPartEndpoint)],
        ["save_assembly"] = [typeof(CreateAssemblyEndpoint), typeof(EditAssemblyEndpoint)],
        ["list_fee_profiles"] = [typeof(GetFeesEndpoint)],
        ["quote_assembly"] = [typeof(QuoteAssemblyEndpoint), typeof(GetFeesEndpoint)],
        ["save_fee_profile"] = [typeof(CreateFeeProfileEndpoint), typeof(EditFeeProfileEndpoint)],
        ["list_maintenance"] = [typeof(ListMaintenanceTasksEndpoint), typeof(ListMaintenanceEnrollmentsEndpoint)],
        ["save_maintenance_task"] = [typeof(CreateMaintenanceTaskEndpoint), typeof(EditMaintenanceTaskEndpoint)],
        ["assign_maintenance"] = [typeof(EnrollPrintersEndpoint), typeof(RebaselineMaintenanceEndpoint)],
        ["complete_maintenance"] = [typeof(CompleteMaintenanceEndpoint)],
        ["list_filaments"] = [typeof(ListFilamentsEndpoint), typeof(ListSpoolsEndpoint)],
        ["list_spools"] = [typeof(ListSpoolsEndpoint)],
        ["register_spools"] = [typeof(RegisterSpoolEndpoint)],
        ["get_farm_stats"] = [typeof(StatsEndpoint)],
    };

    private static readonly string[] LibraryTools = ["search_library", "create_library_folder", "rename_library_folder", "move_library_items"];

    [Fact]
    public void EveryToolNeedsExactlyTheUnionOfItsMirroredEndpointsPermissions()
    {
        McpTools.All.Select(t => t.Name).Should().BeEquivalentTo(Mirrors.Keys.Append("get_farm_overview"));
        McpTools.Find("get_farm_overview")!.Permissions.Should().BeEmpty("each overview section checks its own permissions");
        foreach (var (tool, endpoints) in Mirrors)
        {
            var expected = endpoints.Select(e => e.GetCustomAttribute<PermissionAttribute>()!)
                .SelectMany(a => a.AdditionalPermissions.Prepend(a.Permission)).Distinct().ToList();
            if (expected.Any(p => p.StartsWith("library:", StringComparison.Ordinal))) LibraryTools.Should().Contain(tool);
            McpTools.Find(tool)!.Permissions.Should().BeEquivalentTo(expected, tool);
        }
    }

    [Fact]
    public void EveryToolReturnsAnObject_AsMcpStructuredContentRequires()
    {
        foreach (var tool in McpTools.All)
            tool.Tool.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString().Should().Be("object", tool.Name);
    }

    [Fact]
    public void ToolSchemasGiveEachBranchOneType_AndInlineTheirRefs()
    {
        foreach (var tool in McpTools.All)
        {
            Unportable(tool.Tool.ProtocolTool.InputSchema).Should().BeEmpty(tool.Name);
            Unportable(tool.Tool.ProtocolTool.OutputSchema!.Value).Should().BeEmpty(tool.Name);
        }
    }

    [Fact]
    public void OutputSchemasRequireOnlyFieldsThatAreNeverLeftOut()
    {
        // Results omit null fields, and any string, list or object can be null at runtime; a client rejects a result missing a required field.
        foreach (var tool in McpTools.All)
            OmittableRequired(tool.Tool.ProtocolTool.OutputSchema!.Value).Should().BeEmpty(tool.Name);
    }

    private static IEnumerable<string> OmittableRequired(JsonElement node, string path = "#")
    {
        if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray())
                foreach (var problem in OmittableRequired(item, path)) yield return problem;
        if (node.ValueKind != JsonValueKind.Object) yield break;
        if (node.TryGetProperty("required", out var required) && node.TryGetProperty("properties", out var properties))
            foreach (var name in required.EnumerateArray().Select(n => n.GetString()!))
            {
                var property = properties.GetProperty(name);
                var type = property.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var format = property.TryGetProperty("format", out var f) ? f.GetString() : null;
                if (!(type is "integer" or "number" or "boolean" || (type == "string" && format is "uuid" or "date-time" or "date")))
                    yield return $"{path}/{name} ({type ?? "untyped"}{(format is null ? "" : " " + format)})";
            }
        foreach (var property in node.EnumerateObject())
            foreach (var problem in OmittableRequired(property.Value, $"{path}/{property.Name}")) yield return problem;
    }

    private static IEnumerable<string> Unportable(JsonElement node, string path = "#")
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in node.EnumerateArray())
                foreach (var problem in Unportable(item, $"{path}/{i++}")) yield return problem;
        }
        if (node.ValueKind != JsonValueKind.Object) yield break;
        if (node.TryGetProperty("$ref", out _)) yield return $"{path}: $ref";
        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array) yield return $"{path}: type {type}";
        if (node.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object && !items.EnumerateObject().Any())
            yield return $"{path}: items accepts anything";
        foreach (var property in node.EnumerateObject())
            foreach (var problem in Unportable(property.Value, $"{path}/{property.Name}")) yield return problem;
    }

    [Fact]
    public async Task EveryReadPermissionListsExactlyTheReadTools()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        // plan_production previews with the permission that queueing needs, so it is left out with queue_production.
        tools.Select(t => t.Name).Should().BeEquivalentTo(McpTools.All
            .Where(t => t.Tool.ProtocolTool.Annotations?.ReadOnlyHint == true && t.Name != "plan_production").Select(t => t.Name)).And.HaveCount(16);
        tools.Should().OnlyContain(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true);
    }

    [Theory]
    [InlineData("get_farm_overview")]
    [InlineData("list_printers")]
    [InlineData("list_print_jobs")]
    [InlineData("list_production_runs")]
    [InlineData("list_filaments")]
    [InlineData("list_spools")]
    [InlineData("list_maintenance")]
    [InlineData("list_fee_profiles")]
    public async Task ListToolsReturnTheirResponse(string tool)
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync(tool);

        result.IsError.Should().NotBe(true, Text(result));
        result.StructuredContent!.Value.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task ListToolsTakeTheirFilters()
    {
        await using var client = await ConnectAsync();

        var external = await client.CallToolAsync("list_print_jobs", new Dictionary<string, object?> { ["view"] = "external", ["status"] = "Completed", ["page"] = 2, ["pageSize"] = 500 });
        external.IsError.Should().NotBe(true, Text(external));
        external.StructuredContent!.Value.GetProperty("pageSize").GetInt32().Should().Be(100);
        var spools = await client.CallToolAsync("list_spools", new Dictionary<string, object?> { ["state"] = "using", ["search"] = "pla" });
        spools.IsError.Should().NotBe(true, Text(spools));
        var parts = await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["library"] = "parts", ["query"] = "bracket" });
        parts.IsError.Should().NotBe(true, Text(parts));
        var assemblies = await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["library"] = "assemblies" });
        assemblies.IsError.Should().NotBe(true, Text(assemblies));
        var forecast = await client.CallToolAsync("list_filaments", new Dictionary<string, object?> { ["atRiskOnly"] = true, ["coverDays"] = 42 });
        forecast.StructuredContent!.Value.GetProperty("coverDays").GetInt32().Should().Be(42);

        ErrorCode(await client.CallToolAsync("list_print_jobs", new Dictionary<string, object?> { ["status"] = "Nope" })).Should().Be("INVALID_INPUT");
        ErrorCode(await client.CallToolAsync("list_production_runs", new Dictionary<string, object?> { ["status"] = "Nope" })).Should().Be("INVALID_INPUT");
        ErrorCode(await client.CallToolAsync("list_printers", new Dictionary<string, object?> { ["status"] = "Nope" })).Should().Be("INVALID_INPUT");
        ErrorCode(await client.CallToolAsync("list_filaments", new Dictionary<string, object?> { ["coverDays"] = 0 })).Should().Be("INVALID_INPUT");
        ErrorCode(await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["library"] = "parts", ["folderId"] = Guid.NewGuid() })).Should().Be("NOT_FOUND");
        ErrorCode(await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["library"] = "files" })).Should().Be("INVALID_INPUT");
    }

    [Theory]
    [InlineData("get_printer", "printerId")]
    [InlineData("get_print_job", "jobId")]
    [InlineData("get_production_run", "productionRunId")]
    [InlineData("get_part", "partId")]
    [InlineData("get_assembly", "assemblyId")]
    [InlineData("quote_assembly", "assemblyId")]
    public async Task GetToolsReportAMissingItemAsNotFound_WithTheToolThatFindsIds(string tool, string idArgument)
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync(tool, new Dictionary<string, object?> { [idArgument] = Guid.NewGuid() });

        ErrorCode(result).Should().Be("NOT_FOUND");
        Text(result).Should().Contain("Use ");
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

    [Fact]
    public async Task OverviewLeavesOutSectionsTheConnectionCannotRead()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        await using var client = await McpTestClient.ConnectAsync(factory, (await McpTestClient.TokenAsync(browser, "printers:list")).Credential);

        var overview = (await client.CallToolAsync("get_farm_overview")).StructuredContent!.Value;

        overview.GetProperty("currency").GetString().Should().NotBeNullOrEmpty();
        overview.GetProperty("printers").ValueKind.Should().Be(JsonValueKind.Object);
        (!overview.TryGetProperty("queue", out var queue) || queue.ValueKind == JsonValueKind.Null).Should().BeTrue();
        overview.GetProperty("hidden").EnumerateArray().Select(h => h.GetString()).Should()
            .BeEquivalentTo("queue", "runs", "filament", "filamentNotDeducted", "maintenance");
    }

    [Fact]
    public async Task LibraryToolsNeedTheLibrarysReadPermission_AsTheRestRouteDoes()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        await using var client = await McpTestClient.ConnectAsync(factory, (await McpTestClient.TokenAsync(browser, "library:view", "assemblies:view")).Credential);

        (await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["library"] = "assemblies" })).IsError.Should().NotBe(true);
        ErrorCode(await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["library"] = "parts" })).Should().Be("FORBIDDEN");
    }

    private async Task<McpClient> ConnectAsync()
    {
        var browser = await McpTestClient.BrowserAsync(factory);
        var token = await McpTestClient.TokenAsync(browser, [.. McpTools.All.Where(t => t.Tool.ProtocolTool.Annotations?.ReadOnlyHint == true && t.Name != "plan_production")
            .SelectMany(t => t.Permissions).Append("parts:list").Distinct()]);
        return await McpTestClient.ConnectAsync(factory, token.Credential);
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private static string? ErrorCode(CallToolResult result)
    {
        result.IsError.Should().BeTrue(Text(result));
        return JsonDocument.Parse(Text(result)).RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}
