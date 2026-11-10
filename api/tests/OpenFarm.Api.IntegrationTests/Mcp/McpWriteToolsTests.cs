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
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Mcp;

public sealed class McpWriteToolsTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
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
        writes.Select(t => t.Name).Should().BeEquivalentTo("reorder_print_job", "pin_print_job", "unpin_print_job",
            "requeue_print_job", "dismiss_notification", "complete_maintenance", "pause_print_job", "cancel_print_job");
        writes.Should().OnlyContain(t => t.Tool.ProtocolTool.Annotations!.DestructiveHint != null && t.Tool.ProtocolTool.Annotations.ReadOnlyHint == false);
        foreach (var name in new[] { "requeue_print_job", "pause_print_job", "cancel_print_job" })
        {
            var tool = McpTools.Find(name)!.Tool.ProtocolTool;
            tool.Description.Should().Contain("present").And.Contain("reviewed confirmation");
            tool.Annotations!.OpenWorldHint.Should().BeTrue();
        }
        McpInstructions.Text.Should().Contain("obtain reviewed confirmation").And.Contain("expectedPhase");
    }
}
