using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Api.Mcp;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Mcp;

public sealed class McpWriteToolsTests(McpTestFactory factory) : ProductionTestContext(factory), IClassFixture<McpTestFactory>
{
    private async Task<McpClient> ConnectAsync(HttpClient browser, params string[] permissions) =>
        await McpTestClient.ConnectAsync(Factory, (await McpTestClient.TokenAsync(browser, permissions)).Credential);

    private static JsonElement ErrorOf(CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        return JsonDocument.Parse(((TextContentBlock)result.Content.Single()).Text).RootElement.GetProperty("error");
    }

    [Fact]
    public async Task CancelRefusesAJobThatWasSentToAPrinterAfterItWasRead()
    {
        var browser = await ClientAsync();
        var run = await RunAsync(browser, (await PartAsync()).Id);
        var jobId = (await JobsAsync(run.Id)).Single().Id;
        var printer = await PrinterAsync(manual: true);
        await using var client = await ConnectAsync(browser, "print-jobs:view", "print-jobs:cancel");

        var read = await client.CallToolAsync("get_print_job", new Dictionary<string, object?> { ["jobId"] = jobId });
        read.IsError.Should().NotBe(true, string.Join("", read.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        read.StructuredContent!.Value.GetProperty("phase").GetString().Should().Be("Queued");
        await AssignAsync(jobId, printer.Id, started: false);

        var error = ErrorOf(await client.CallToolAsync("cancel_print_job",
            new Dictionary<string, object?> { ["jobId"] = jobId, ["expectedPhase"] = "queued" }));
        error.GetProperty("code").GetString().Should().Be("CONFLICT");
        error.GetProperty("currentPhase").GetString().Should().Be("Starting");
        (await JobsAsync(run.Id)).Single().Should().Match<PrintJob>(j => j.Status == PrintJobStatus.Queued && j.CancelRequestedAtUtc == null);

        var cancelled = await client.CallToolAsync("cancel_print_job",
            new Dictionary<string, object?> { ["jobId"] = jobId, ["expectedPhase"] = "Starting" });
        cancelled.IsError.Should().NotBe(true);
        cancelled.StructuredContent!.Value.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        ErrorOf(await client.CallToolAsync("cancel_print_job",
            new Dictionary<string, object?> { ["jobId"] = jobId, ["expectedPhase"] = "Done" })).GetProperty("code").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task McpPauseWritesTheSameActivityRowAsRest_AttributedToTheToken()
    {
        var browser = await ClientAsync();
        var printer = await PrinterAsync(manual: true);
        var run = await RunAsync(browser, (await PartAsync()).Id, quantity: 2);
        var jobs = await JobsAsync(run.Id);
        await AssignAsync(jobs[0].Id, printer.Id);
        await using var client = await ConnectAsync(browser, "print-jobs:remote-control");

        var paused = await client.CallToolAsync("pause_print_job", new Dictionary<string, object?> { ["jobId"] = jobs[0].Id });
        paused.IsError.Should().NotBe(true);
        paused.StructuredContent!.Value.GetProperty("job").GetProperty("status").GetString().Should().Be("Paused");
        paused.StructuredContent.Value.GetProperty("confirmed").GetBoolean().Should().BeTrue();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var row = await db.ActivityLogEntries.AsNoTracking().SingleAsync(a => a.EntityId == jobs[0].Id.ToString() && a.EventType == "PrintJobPauseRequested");
        row.EntityType.Should().Be("PrintJob");
        row.Details!.Via!.Kind.Should().Be("Token");

        var conflict = ErrorOf(await client.CallToolAsync("pause_print_job", new Dictionary<string, object?> { ["jobId"] = jobs[1].Id }));
        conflict.GetProperty("code").GetString().Should().Be("CONFLICT");
    }

    [Fact]
    public async Task InvalidWriteInputComesBackAsInvalidInput()
    {
        var browser = await ClientAsync();
        await using var client = await ConnectAsync(browser, "maintenance:complete");

        var error = ErrorOf(await client.CallToolAsync("complete_maintenance", new Dictionary<string, object?> { ["enrollmentIds"] = Array.Empty<Guid>() }));

        error.GetProperty("code").GetString().Should().Be("INVALID_INPUT");
        error.GetProperty("issues").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void WritesDeclareTheirEffects_AndPrinterActionsRequireReviewedConfirmation()
    {
        var writes = McpTools.All.Where(t => t.Tool.ProtocolTool.Annotations?.ReadOnlyHint != true).ToList();
        writes.Select(t => t.Name).Should().BeEquivalentTo("mark_printers_ready", "reorder_print_job", "pin_print_job",
            "requeue_print_job", "pause_print_job", "cancel_print_job", "queue_production", "create_library_folder", "rename_library_folder",
            "move_library_items", "update_parts", "save_assembly", "save_fee_profile", "save_maintenance_task", "assign_maintenance",
            "complete_maintenance", "register_spools");
        writes.Should().OnlyContain(t => t.Tool.ProtocolTool.Annotations!.DestructiveHint != null && t.Tool.ProtocolTool.Annotations.ReadOnlyHint == false);
        foreach (var name in new[] { "requeue_print_job", "pause_print_job", "cancel_print_job" })
        {
            var tool = McpTools.Find(name)!.Tool.ProtocolTool;
            tool.Description.Should().Contain("present").And.Contain("reviewed confirmation");
            tool.Annotations!.OpenWorldHint.Should().BeTrue();
        }
        McpTools.Find("mark_printers_ready")!.Tool.ProtocolTool.Description.Should().Contain("the user says");
        McpTools.Find("queue_production")!.Tool.ProtocolTool.Description.Should().Contain("after the user approved");
        McpInstructions.Text.Should().Contain("obtain reviewed confirmation").And.Contain("expectedPhase").And.Contain("plan_production");
    }

    [Fact]
    public async Task MarkReady_ClearsOnlyThePlateTheUserSaw()
    {
        var browser = await ClientAsync();
        var seen = await AwaitingClearAsync();
        var changed = await AwaitingClearAsync();
        var idle = await PrinterAsync();
        await using var client = await ConnectAsync(browser, "printers:list", "printers:mark-ready");
        var read = (await client.CallToolAsync("list_printers", new Dictionary<string, object?> { ["status"] = "AwaitingClear" })).StructuredContent!.Value;
        var since = read.GetProperty("printers").EnumerateArray().Single(p => p.GetProperty("printerId").GetGuid() == seen.Id).GetProperty("awaitingClearSinceUtc").GetDateTimeOffset();

        var result = await client.CallToolAsync("mark_printers_ready", new Dictionary<string, object?>
        {
            ["printers"] = new object[]
            {
                new { printerId = seen.Id, awaitingClearSinceUtc = since },
                new { printerId = changed.Id, awaitingClearSinceUtc = since.AddMinutes(-5) },
                new { printerId = idle.Id, awaitingClearSinceUtc = since },
            },
        });

        result.IsError.Should().NotBe(true, string.Join("", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        result.StructuredContent!.Value.GetProperty("printers").EnumerateArray().Select(r => r.GetProperty("outcome").GetString())
            .Should().Equal("Ready", "ChangedSinceRead", "NotAwaitingClear");
        (await PrinterStatusAsync(seen.Id)).Should().Be(PrinterStatus.Idle);
        (await PrinterStatusAsync(changed.Id)).Should().Be(PrinterStatus.AwaitingClear);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await db.ActivityLogEntries.AsNoTracking().SingleAsync(a => a.EntityId == seen.Id.ToString() && a.EventType == "PrinterMarkedReady"))
            .Details!.Via!.Kind.Should().Be("Token");
    }

    [Fact]
    public async Task QueueProduction_QueuesThePlanOnce_AndRefusesAnAlteredToken()
    {
        var browser = await ClientAsync();
        var part = await PartAsync();
        await using var client = await ConnectAsync(browser, "production-runs:create");

        var plan = await client.CallToolAsync("plan_production", new Dictionary<string, object?>
        {
            ["lines"] = new object[] { new { kind = "Part", id = part.Id, quantity = 3 } },
        });
        plan.IsError.Should().NotBe(true, string.Join("", plan.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        var preview = plan.StructuredContent!.Value;
        preview.GetProperty("lines")[0].GetProperty("plates").GetInt32().Should().Be(3);
        var token = preview.GetProperty("planToken").GetString()!;
        await DemandCountAsync(part.Id).ContinueWith(t => t.Result.Should().Be(0));

        var first = await client.CallToolAsync("queue_production", new Dictionary<string, object?> { ["planToken"] = token });
        var again = await client.CallToolAsync("queue_production", new Dictionary<string, object?> { ["planToken"] = token });

        first.StructuredContent!.Value.GetProperty("lines")[0].GetProperty("outcome").GetString().Should().Be("Queued");
        again.StructuredContent!.Value.GetProperty("lines")[0].GetProperty("outcome").GetString().Should().Be("Queued");
        (await DemandCountAsync(part.Id)).Should().Be(1);
        ErrorOf(await client.CallToolAsync("queue_production", new Dictionary<string, object?> { ["planToken"] = token[..^4] + "AAAA" }))
            .GetProperty("code").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task QueueProduction_CanMakeANamedRunOfSeveralLines()
    {
        var browser = await ClientAsync();
        var (a, b) = (await PartAsync(), await PartAsync());
        await using var client = await ConnectAsync(browser, "production-runs:create");

        var plan = (await client.CallToolAsync("plan_production", new Dictionary<string, object?>
        {
            ["lines"] = new object[] { new { kind = "Part", id = a.Id, quantity = 2 }, new { kind = "part", id = b.Id, quantity = 1, basis = "plates" } },
            ["destination"] = "newRun",
            ["runName"] = "Market stall",
        })).StructuredContent!.Value;
        var queued = (await client.CallToolAsync("queue_production", new Dictionary<string, object?> { ["planToken"] = plan.GetProperty("planToken").GetString() }))
            .StructuredContent!.Value;

        queued.GetProperty("run").GetString().Should().Be("Market stall");
        var run = await DetailAsync(browser, queued.GetProperty("productionRunId").GetGuid());
        run.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task LibraryFeeMaintenanceAndSpoolWrites_LogLikeRest()
    {
        var browser = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await using var client = await ConnectAsync(browser, "library:manage", "parts:list", "parts:edit", "settings:fees:update",
            "maintenance:manage", "spools:register");

        var folder = (await client.CallToolAsync("create_library_folder", new Dictionary<string, object?> { ["library"] = "parts", ["path"] = "Storage/Bins" })).StructuredContent!.Value;
        var again = (await client.CallToolAsync("create_library_folder", new Dictionary<string, object?> { ["library"] = "parts", ["path"] = "storage/bins" })).StructuredContent!.Value;
        again.GetProperty("folderId").GetGuid().Should().Be(folder.GetProperty("folderId").GetGuid());
        folder.GetProperty("path").GetString().Should().Be("Storage/Bins");
        (await client.CallToolAsync("move_library_items", new Dictionary<string, object?>
        { ["library"] = "parts", ["itemIds"] = new[] { part.Id }, ["toFolderId"] = folder.GetProperty("folderId").GetGuid() })).IsError.Should().NotBe(true);
        var tagged = await client.CallToolAsync("update_parts", new Dictionary<string, object?>
        { ["updates"] = new object[] { new { partId = part.Id, addTags = BinsTag } } });
        tagged.IsError.Should().NotBe(true, string.Join("", tagged.Content.OfType<TextContentBlock>().Select(c => c.Text)));

        var fees = await client.CallToolAsync("save_fee_profile", new Dictionary<string, object?>
        { ["name"] = $"Shop {Guid.NewGuid():N}", ["fees"] = new object[] { new { name = "Commission", type = "percent", amount = 18 }, new { name = "Order", type = "Fixed", amount = 0.5 } } });
        fees.StructuredContent!.Value.GetProperty("percentFees").GetDecimal().Should().Be(18);

        var task = (await client.CallToolAsync("save_maintenance_task", new Dictionary<string, object?> { ["name"] = "Lubricate rods", ["everyPrintHours"] = 200 }))
            .StructuredContent!.Value.GetProperty("id").GetGuid();
        var assigned = (await client.CallToolAsync("assign_maintenance", new Dictionary<string, object?> { ["taskId"] = task, ["printerIds"] = new[] { printer.Id } }))
            .StructuredContent!.Value;
        assigned.GetProperty("enrolled").GetArrayLength().Should().Be(1);

        Guid variantId;
        using (var scope = Factory.Services.CreateScope())
            variantId = await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().FilamentVariants.Select(v => v.Id).FirstAsync();
        var spools = await client.CallToolAsync("register_spools", new Dictionary<string, object?>
        { ["lines"] = new object[] { new { filamentVariantId = variantId, quantity = 2, spoolWeightGrams = 1000, price = 22 } }, ["supplier"] = "Shop" });
        spools.StructuredContent!.Value.GetProperty("count").GetInt32().Should().Be(2);

        using var check = Factory.Services.CreateScope();
        var events = await check.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ActivityLogEntries.AsNoTracking()
            .Where(a => a.Details!.Via!.Kind == "Token").Select(a => a.EventType).ToListAsync();
        events.Should().Contain(["LibraryFolderCreated", "LibraryItemsMoved", "PartEdited", "FeeProfileCreated", "MaintenanceTaskCreated",
            "MaintenanceEnrolled", "SpoolRegistered"]);
    }

    [Fact]
    public async Task SaveAssemblyThenQuoteIt_WithATriedRule()
    {
        var browser = await ClientAsync();
        var part = await PartAsync();
        await using var client = await ConnectAsync(browser, "assemblies:create", "assemblies:edit", "assemblies:view", "settings:fees:read");

        var saved = await client.CallToolAsync("save_assembly", new Dictionary<string, object?>
        {
            ["name"] = $"Kit {Guid.NewGuid():N}",
            ["lines"] = new object[] { new { kind = "part", id = part.Id, quantity = 2 } },
            ["pricingMode"] = "Price",
            ["pricingValue"] = 12,
        });
        saved.IsError.Should().NotBe(true, string.Join("", saved.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        var assemblyId = saved.StructuredContent!.Value.GetProperty("assemblyId").GetGuid();

        var quote = (await client.CallToolAsync("quote_assembly", new Dictionary<string, object?>
        { ["assemblyId"] = assemblyId, ["pricingMode"] = "markup", ["pricingValue"] = 100 })).StructuredContent!.Value;

        quote.GetProperty("pricing").GetString().Should().Be("Markup 100%");
        quote.GetProperty("profiles").GetArrayLength().Should().BeGreaterThan(0);
        quote.GetProperty("costs").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Contain("Filament");
    }

    private static readonly string[] BinsTag = ["bins"];

    private async Task<Printer> AwaitingClearAsync()
    {
        var printer = await PrinterAsync(state: "RUNNING");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await db.Printers.SingleAsync(p => p.Id == printer.Id)).ObserveStatus("FINISH");
        await db.SaveChangesAsync();
        (await PrinterStatusAsync(printer.Id)).Should().Be(PrinterStatus.AwaitingClear);
        return printer;
    }

    private async Task<int> DemandCountAsync(Guid partId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().ProductionDemands.CountAsync(d => d.TargetId == partId);
    }
}
