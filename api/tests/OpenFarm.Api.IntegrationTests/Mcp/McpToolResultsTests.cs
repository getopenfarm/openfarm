using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Api.Mcp;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Mcp;

/// <summary>
/// Clients validate a tool's structured result against its output schema and reject the whole call on any mismatch
/// (MCP Inspector, the TypeScript SDK). So every tool runs here on real data, idle and busy printers and empty fields
/// included, and every result must match its own schema. A new tool fails this test until it is added.
/// </summary>
public sealed class McpToolResultsTests(McpTestFactory factory) : ProductionTestContext(factory), IClassFixture<McpTestFactory>
{
    private static readonly string[] BinsTag = ["bins"];
    private static readonly string[] BlankTag = [""];
    private readonly HashSet<string> _called = [];
    private McpClient _client = null!;

    [Fact]
    public async Task EveryToolsResultMatchesItsOutputSchema()
    {
        var browser = await ClientAsync();
        var part = await PartAsync();
        var idle = await PrinterAsync();
        var manual = await PrinterAsync(manual: true);
        var awaiting = await PrinterAsync(state: "RUNNING");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == awaiting.Id)).ObserveStatus("FINISH");
            await db.SaveChangesAsync();
        }
        var run = await RunAsync(browser, part.Id, quantity: 4);
        var jobs = await JobsAsync(run.Id);
        await AssignAsync(jobs[2].Id, manual.Id);
        Guid variantId;
        using (var scope = Factory.Services.CreateScope())
            variantId = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().FilamentVariants.Select(v => v.Id).FirstAsync();
        var token = await McpTestClient.TokenAsync(browser, [.. McpTools.All.SelectMany(t => t.Permissions).Append("parts:list").Append("assemblies:view").Distinct()]);
        await using var client = await McpTestClient.ConnectAsync(Factory, token.Credential);
        _client = client;

        await Call("get_farm_overview", []);
        var printers = await Call("list_printers", []);
        await Call("list_printers", new() { ["status"] = "AwaitingClear" });
        await Call("get_printer", new() { ["printerId"] = idle.Id });
        var since = printers.GetProperty("printers").EnumerateArray().Single(p => p.GetProperty("printerId").GetGuid() == awaiting.Id)
            .GetProperty("awaitingClearSinceUtc").GetDateTimeOffset();
        await Call("mark_printers_ready", new() { ["printers"] = new object[] { new { printerId = awaiting.Id, awaitingClearSinceUtc = since } } });

        await Call("list_print_jobs", []);
        await Call("list_print_jobs", new() { ["view"] = "external" });
        await Call("get_print_job", new() { ["jobId"] = jobs[0].Id });
        await Call("get_print_job", new() { ["jobId"] = jobs[2].Id });
        await Call("reorder_print_job", new() { ["jobId"] = jobs[1].Id });
        await Call("pin_print_job", new() { ["jobId"] = jobs[0].Id, ["printerId"] = idle.Id });
        await Call("pin_print_job", new() { ["jobId"] = jobs[0].Id });
        await Call("pause_print_job", new() { ["jobId"] = jobs[2].Id });
        await Call("cancel_print_job", new() { ["jobId"] = jobs[3].Id, ["expectedPhase"] = "Queued" });
        await Call("requeue_print_job", new() { ["jobId"] = jobs[3].Id });

        var plan = await Call("plan_production", new()
        {
            ["lines"] = new object[] { new { kind = "Part", id = part.Id, quantity = 3 } },
            ["destination"] = "newRun",
            ["runName"] = "Market",
            ["dueDate"] = "2026-12-01",
        });
        await Call("queue_production", new() { ["planToken"] = plan.GetProperty("planToken").GetString() });
        await Call("list_production_runs", []);
        await Call("get_production_run", new() { ["productionRunId"] = run.Id });

        var folderId = (await Call("create_library_folder", new() { ["library"] = "parts", ["path"] = "Storage/Bins" })).GetProperty("folderId").GetGuid();
        await Call("rename_library_folder", new() { ["library"] = "parts", ["folderId"] = folderId, ["name"] = "Boxes" });
        await Call("move_library_items", new() { ["library"] = "parts", ["itemIds"] = new[] { part.Id }, ["toFolderId"] = folderId });
        await Call("update_parts", new() { ["updates"] = new object[] { new { partId = part.Id, name = "Bracket", addTags = BinsTag } } });
        await Call("search_library", new() { ["library"] = "parts" });
        await Call("search_library", new() { ["library"] = "parts", ["query"] = "bracket" });
        await Call("get_part", new() { ["partId"] = part.Id });
        var assemblyId = (await Call("save_assembly", new()
        {
            ["name"] = "Desk set",
            ["lines"] = new object[] { new { kind = "Part", id = part.Id, quantity = 2 } },
            ["pricingMode"] = "Margin",
            ["pricingValue"] = 30,
        })).GetProperty("assemblyId").GetGuid();
        await Call("save_assembly", new() { ["assemblyId"] = assemblyId, ["lines"] = new object[] { new { kind = "Part", id = part.Id, quantity = 3 } } });
        await Call("search_library", new() { ["library"] = "assemblies" });
        await Call("get_assembly", new() { ["assemblyId"] = assemblyId, ["expand"] = true });

        await Call("save_fee_profile", new()
        {
            ["name"] = "Shopee",
            ["fees"] = new object[] { new { name = "Commission", type = "Percent", amount = 18 }, new { name = "Order", type = "Fixed", amount = 0.5 } },
        });
        await Call("list_fee_profiles", []);
        await Call("quote_assembly", new() { ["assemblyId"] = assemblyId });
        await Call("quote_assembly", new() { ["assemblyId"] = assemblyId, ["pricingMode"] = "Markup", ["pricingValue"] = 50 });

        var taskId = (await Call("save_maintenance_task", new() { ["name"] = "Lubricate rods", ["everyPrintHours"] = 200 })).GetProperty("id").GetGuid();
        await Call("assign_maintenance", new() { ["taskId"] = taskId, ["printerIds"] = new[] { idle.Id, manual.Id } });
        var enrollment = (await Call("list_maintenance", [])).GetProperty("enrollments")[0].GetProperty("id").GetGuid();
        await Call("complete_maintenance", new() { ["enrollmentIds"] = new[] { enrollment } });

        await Call("list_filaments", []);
        await Call("list_filaments", new() { ["atRiskOnly"] = true, ["coverDays"] = 42 });
        await Call("register_spools", new()
        {
            ["lines"] = new object[] { new { filamentVariantId = variantId, quantity = 2, spoolWeightGrams = 1000, price = 22 } },
            ["receivedOn"] = "2026-11-10",
        });
        await Call("list_spools", []);
        var to = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
        await Call("get_farm_stats", new() { ["from"] = to.AddDays(-30), ["to"] = to, ["timeZone"] = "UTC" });

        _called.Should().BeEquivalentTo(McpTools.All.Select(t => t.Name), "every tool must be exercised here");
    }

    [Fact]
    public async Task BlankArgumentsMeanNotGiven_AsFormClientsSendThem()
    {
        var browser = await ClientAsync();
        var part = await PartAsync();
        var job = (await JobsAsync((await RunAsync(browser, part.Id)).Id))[0];
        await using var client = await McpTestClient.ConnectAsync(Factory, (await McpTestClient.TokenAsync(browser,
            "printers:list", "print-jobs:view", "print-jobs:pin", "production-runs:view", "spools:list", "parts:edit", "settings:fees:update")).Credential);
        _client = client;

        await Call("list_printers", new() { ["status"] = "" });
        await Call("list_print_jobs", new() { ["view"] = "", ["status"] = " ", ["search"] = null });
        await Call("list_production_runs", new() { ["status"] = "", ["search"] = "" });
        await Call("list_spools", new() { ["state"] = "", ["search"] = "" });
        (await Call("pin_print_job", new() { ["jobId"] = job.Id, ["printerId"] = "" })).GetProperty("pinned").GetBoolean().Should().BeFalse();
        var renamed = await Call("update_parts", new() { ["updates"] = new object[] { new { partId = part.Id, name = "", addTags = BlankTag } } });
        renamed.GetProperty("parts")[0].GetProperty("name").GetString().Should().Be(part.Name, "a blank name keeps the saved one");

        var missing = await client.CallToolAsync("save_fee_profile", new Dictionary<string, object?> { ["name"] = "", ["fees"] = Array.Empty<object>() });
        missing.IsError.Should().BeTrue();
        string.Join("", missing.Content.OfType<TextContentBlock>().Select(c => c.Text)).Should().Contain("INVALID_INPUT").And.Contain("name");
    }

    private async Task<JsonElement> Call(string tool, Dictionary<string, object?> arguments)
    {
        _called.Add(tool);
        var result = await _client.CallToolAsync(tool, arguments);
        result.IsError.Should().NotBe(true, $"{tool}: {string.Join("", result.Content.OfType<TextContentBlock>().Select(c => c.Text))}");
        Mismatches(McpTools.Find(tool)!.Tool.ProtocolTool.OutputSchema!.Value, result.StructuredContent!.Value, tool).Should().BeEmpty();
        return result.StructuredContent.Value;
    }

    /// <summary>What a client's validator checks that the server can get wrong: required fields and types.</summary>
    private static IEnumerable<string> Mismatches(JsonElement schema, JsonElement value, string path)
    {
        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            if (!anyOf.EnumerateArray().Any(branch => !Mismatches(branch, value, path).Any())) yield return $"{path}: matches no anyOf branch";
            yield break;
        }
        if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && !IsType(type.GetString()!, value))
        {
            yield return $"{path}: {value.ValueKind} is not {type.GetString()}";
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray())
                    if (!value.TryGetProperty(name.GetString()!, out _)) yield return $"{path}: missing {name}";
            if (schema.TryGetProperty("properties", out var properties))
                foreach (var property in properties.EnumerateObject())
                    if (value.TryGetProperty(property.Name, out var child))
                        foreach (var mismatch in Mismatches(property.Value, child, $"{path}/{property.Name}")) yield return mismatch;
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var i = 0;
            foreach (var item in value.EnumerateArray())
                foreach (var mismatch in Mismatches(items, item, $"{path}/{i++}")) yield return mismatch;
        }
    }

    private static bool IsType(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d) && d == decimal.Truncate(d),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };
}
