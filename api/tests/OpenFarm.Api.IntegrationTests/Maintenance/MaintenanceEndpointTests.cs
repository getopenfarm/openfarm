using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.ActivityLog;
using OpenFarm.Api.Endpoints.Maintenance;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.Maintenance;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Maintenance;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Maintenance;

// Test-after. Hosted services are off (ProductionTestFactory), so printer activity changes only where a test writes it.
public class MaintenanceEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static readonly DateTimeOffset Now = new(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
    private static readonly DateTimeOffset Since = Now.AddDays(-10);

    [Fact]
    public async Task Usage_ClipsSpansToTheWindow_CountsTheOpenPrintingStretch_AndOnlyPrintingOnThisPrinter()
    {
        var printer = await QuietPrinterAsync(PrinterActivity.Printing, Now.AddMinutes(-30));
        var other = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        await SpanAsync(printer, PrinterActivity.Printing, Since.AddHours(-2), Since.AddHours(1));   // straddles since: 1 h
        await SpanAsync(printer, PrinterActivity.Paused, Since.AddHours(1), Since.AddHours(3));
        await SpanAsync(printer, PrinterActivity.Unknown, Since.AddHours(3), Since.AddHours(5));    // untracked
        await SpanAsync(other, PrinterActivity.Printing, Since.AddHours(1), Since.AddHours(4));

        var usage = await UsageAsync(printer, Since, Now);

        usage.Should().Be(new MaintenanceMeasuredUsage(1.5m, 0, 2m));
    }

    [Fact]
    public async Task AnExternalPrintWithoutSpans_CountsHoursAndPrints_AndABackdatedCompletionMeasuresOnlyUpToItsTime()
    {
        var client = await ClientAsync();
        var printer = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        await EndedPrintAsync(printer, Since.AddHours(1), Since.AddHours(4));
        await EndedPrintAsync(printer, Since.AddHours(3), Since.AddHours(3.5));

        (await UsageAsync(printer, Since, Now)).Should().Be(new MaintenanceMeasuredUsage(3.5m, 2, 0));
        (await UsageAsync(printer, Since, Since.AddHours(2))).Should().Be(new MaintenanceMeasuredUsage(1m, 1, 0));

        var task = await CreateTaskAsync(client, new { everyPrintHours = 100m, printerIds = new[] { printer }, lastDoneAtUtc = Since });
        var enrollment = (await EnrollmentsAsync(client, $"taskId={task.Id}")).Single();
        enrollment.Should().BeEquivalentTo(new { Hours = 3.5m, Prints = 2, State = "Ok", DrivingTrigger = "Hours", Interval = 100m });

        var done = await client.PostAsJsonAsync("/api/v1/maintenance/enrollments/complete",
            new { enrollmentIds = new[] { enrollment.Id }, performedAtUtc = Since.AddHours(2), cost = 5m });
        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());

        var log = (await client.GetFromJsonAsync<MaintenanceLogsResponse>($"/api/v1/maintenance/logs?taskId={task.Id}"))!.Items.Single();
        log.Should().BeEquivalentTo(new { HoursAtCompletion = 1m, PrintsAtCompletion = 1, Cost = 5m, PerformedAtUtc = Since.AddHours(2), PerformedByName = "admin" },
            o => o.Using<DateTimeOffset>(c => c.Subject.Should().Be(c.Expectation)).WhenTypeIs<DateTimeOffset>());
        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Single()
            .Should().BeEquivalentTo(new { Hours = 2.5m, Prints = 1, LastDoneAtUtc = (DateTimeOffset?)Since.AddHours(2) });
    }

    [Fact]
    public async Task Tasks_CreateEditDelete_EditAppliesToEveryEnrolledPrinter_AndIsLogged()
    {
        var client = await ClientAsync();
        var printers = new[] { await QuietPrinterAsync(PrinterActivity.Idle, Since), await QuietPrinterAsync(PrinterActivity.Idle, Since) };
        var name = $"Lube rods {Guid.NewGuid():N}";

        var task = await CreateTaskAsync(client, new { name, everyDays = 10, printerIds = printers, lastDoneAtUtc = Now.AddDays(-2) });
        task.PrinterIds.Should().BeEquivalentTo(printers);
        task.WorstState.Should().Be("Ok");
        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Should().HaveCount(2).And.OnlyContain(e => e.Interval == 10 && e.DrivingTrigger == "Days");

        var duplicate = await client.PostAsJsonAsync("/api/v1/maintenance/tasks", new { name = name.ToUpperInvariant(), everyDays = 1 });
        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync("/api/v1/maintenance/tasks", new { name = "No interval" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var edited = await client.PutAsJsonAsync($"/api/v1/maintenance/tasks/{task.Id}", new { name, everyDays = 1, warnAtPercent = 50 });
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        (await edited.Content.ReadFromJsonAsync<MaintenanceTaskResponse>())!.WorstState.Should().Be("Overdue");
        (await EnrollmentsAsync(client, $"taskId={task.Id}&state=attention"))
            .Should().HaveCount(2).And.OnlyContain(e => e.State == "Overdue" && e.Interval == 1 && e.Remaining <= -1);

        (await client.DeleteAsync($"/api/v1/maintenance/tasks/{task.Id}/enrollments/{printers[1]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Should().ContainSingle().Which.PrinterId.Should().Be(printers[0]);

        (await client.DeleteAsync($"/api/v1/maintenance/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<MaintenanceTasksResponse>("/api/v1/maintenance/tasks"))!.Items.Should().NotContain(t => t.Id == task.Id);
        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Should().BeEmpty();

        var taskLog = await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityType=MaintenanceTask&entityId={task.Id}");
        taskLog!.Items.Select(e => e.EventType).Should().BeEquivalentTo("MaintenanceTaskCreated", "MaintenanceTaskEdited", "MaintenanceTaskDeleted");
        var printerLog = await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityType=Printer&entityId={printers[1]}");
        printerLog!.Items.Should().Contain(e => e.EventType == "MaintenanceUnenrolled");
    }

    [Fact]
    public async Task BulkMarkDone_IsAllOrNone()
    {
        var client = await ClientAsync();
        var printers = new[] { await QuietPrinterAsync(PrinterActivity.Idle, Since), await QuietPrinterAsync(PrinterActivity.Idle, Since) };
        var task = await CreateTaskAsync(client, new { everyDays = 30, printerIds = printers, lastDoneAtUtc = Now.AddDays(-5) });
        var enrollments = await EnrollmentsAsync(client, $"taskId={task.Id}");
        var late = enrollments.Single(e => e.PrinterId == printers[1]);
        (await client.PutAsJsonAsync($"/api/v1/maintenance/enrollments/{late.Id}/baseline", new { lastDoneAtUtc = Now.AddDays(-1), carryHours = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var failed = await client.PostAsJsonAsync("/api/v1/maintenance/enrollments/complete",
            new { enrollmentIds = enrollments.Select(e => e.Id), performedAtUtc = Now.AddDays(-2) });

        failed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var failures = (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("failures");
        failures.EnumerateObject().Select(f => f.Name).Should().Equal(late.Id.ToString());
        (await client.GetFromJsonAsync<MaintenanceLogsResponse>($"/api/v1/maintenance/logs?taskId={task.Id}"))!.TotalCount.Should().Be(0);
        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Single(e => e.PrinterId == printers[0]).CycleStartedAtUtc.Should().Be(Now.AddDays(-5));

        var done = await client.PostAsJsonAsync("/api/v1/maintenance/enrollments/complete",
            new { enrollmentIds = enrollments.Select(e => e.Id), notes = "Cleaned", performedByNote = "Sam" });
        done.StatusCode.Should().Be(HttpStatusCode.OK);
        (await done.Content.ReadFromJsonAsync<MaintenanceChangeResponse>())!.Count.Should().Be(2);
        var logs = (await client.GetFromJsonAsync<MaintenanceLogsResponse>($"/api/v1/maintenance/logs?taskId={task.Id}"))!;
        logs.Items.Should().HaveCount(2).And.OnlyContain(l => l.Notes == "Cleaned" && l.PerformedByNote == "Sam");
        logs.Items.Single(l => l.PrinterId == printers[1]).HoursAtCompletion.Should().Be(2m, "the carry is part of the completed cycle");
        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Should().OnlyContain(e => e.CarryHours == 0 && e.LastDoneAtUtc != null);
        var printerLog = await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityType=Printer&entityId={printers[0]}");
        printerLog!.Items.Should().Contain(e => e.EventType == "MaintenanceCompleted");
    }

    [Fact]
    public async Task ALog_OutlivesItsPrinter()
    {
        var client = await ClientAsync();
        var printer = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        var printerName = await PrinterNameAsync(printer);
        var task = await CreateTaskAsync(client, new { everyPrints = 50, printerIds = new[] { printer }, lastDoneAtUtc = Since });
        var enrollment = (await EnrollmentsAsync(client, $"printerId={printer}")).Single();
        (await client.PostAsJsonAsync("/api/v1/maintenance/enrollments/complete", new { enrollmentIds = new[] { enrollment.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.DeleteAsync($"/api/v1/printers/{printer}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await EnrollmentsAsync(client, $"taskId={task.Id}")).Should().BeEmpty();
        var log = (await client.GetFromJsonAsync<MaintenanceLogsResponse>($"/api/v1/maintenance/logs?taskId={task.Id}"))!.Items.Single();
        log.PrinterId.Should().BeNull();
        log.EnrollmentId.Should().BeNull();
        log.PrinterName.Should().Be(printerName);
        log.TaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task TheMigration_SeedsTheDefaults_AndADeletedDefaultStaysDeleted()
    {
        var client = await ClientAsync();
        var tasks = (await client.GetFromJsonAsync<MaintenanceTasksResponse>("/api/v1/maintenance/tasks"))!.Items
            .Where(t => DefaultMaintenance.Names.Contains(t.Name)).ToDictionary(t => t.Name);

        tasks.Keys.Should().BeEquivalentTo(DefaultMaintenance.Names);
        tasks.Values.Should().OnlyContain(t => t.WarnAtPercent == 80 && t.Notes != null);
        tasks.Values.Select(t => (t.Name, t.EveryPrintHours, t.EveryDays, t.EveryPrints)).Should().BeEquivalentTo(new (string, decimal?, int?, int?)[]
        {
            (DefaultMaintenance.XyAxisHighUse, null, 30, null), (DefaultMaintenance.AirFilter, null, 90, null),
            (DefaultMaintenance.CarbonRods, null, 30, null), (DefaultMaintenance.ZLeadScrew, null, 90, null),
            (DefaultMaintenance.CameraLens, null, 30, null), (DefaultMaintenance.XyAxis, null, 30, null),
            (DefaultMaintenance.Calibration, 100m, null, null), (DefaultMaintenance.RailLubrication, 100m, null, null),
            (DefaultMaintenance.ExtruderGearStandard, null, null, 20), (DefaultMaintenance.ExtruderGearCf, null, null, 8),
        });

        var deleted = tasks[DefaultMaintenance.AirFilter].Id;
        (await client.DeleteAsync($"/api/v1/maintenance/tasks/{deleted}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var printer = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        (await EnrollInTasksAsync(client, printer, [tasks[DefaultMaintenance.Calibration].Id])).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await client.GetFromJsonAsync<MaintenanceTasksResponse>("/api/v1/maintenance/tasks"))!.Items;
        after.Should().NotContain(t => t.Id == deleted || t.Name == DefaultMaintenance.AirFilter);
        (await EnrollInTasksAsync(client, printer, [deleted])).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnrollPrinterInTasks_IsIdempotent_RejectsUnknownTasks_AndNeverCreatesTasks()
    {
        var client = await ClientAsync();
        var printer = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        var other = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        var a = await CreateTaskAsync(client, new { everyDays = 30, printerIds = new[] { other } });
        var b = await CreateTaskAsync(client, new { everyPrints = 10 });
        var taskCount = (await client.GetFromJsonAsync<MaintenanceTasksResponse>("/api/v1/maintenance/tasks"))!.Items.Count;

        var first = await EnrollInTasksAsync(client, printer, [a.Id, b.Id, a.Id]);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        (await first.Content.ReadFromJsonAsync<MaintenanceChangeResponse>())!.Count.Should().Be(2);
        var again = await EnrollInTasksAsync(client, printer, [a.Id, b.Id]);
        (await again.Content.ReadFromJsonAsync<MaintenanceChangeResponse>())!.Count.Should().Be(0);

        (await EnrollmentsAsync(client, $"printerId={printer}")).Select(e => e.TaskId).Should().BeEquivalentTo([a.Id, b.Id]);
        (await EnrollmentsAsync(client, $"printerId={printer}")).Should().OnlyContain(e => e.CarryHours == 0 && e.CarryPrints == 0);
        (await EnrollmentsAsync(client, $"taskId={a.Id}")).Should().HaveCount(2, "the other printer keeps its enrollment");

        var unknown = await EnrollInTasksAsync(client, printer, [b.Id, Guid.NewGuid()]);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await EnrollInTasksAsync(client, Guid.NewGuid(), [a.Id])).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await EnrollInTasksAsync(client, printer, [])).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<MaintenanceTasksResponse>("/api/v1/maintenance/tasks"))!.Items.Should().HaveCount(taskCount);

        var printerLog = await client.GetFromJsonAsync<ActivityLogPageResponse>($"/api/v1/activity-log?entityType=Printer&entityId={printer}");
        printerLog!.Items.Count(e => e.EventType == "MaintenanceEnrolled").Should().Be(1, "the no-op call logs nothing");
    }

    [Fact]
    public async Task AViewer_SeesMaintenance_ButCannotChangeIt()
    {
        var admin = await ClientAsync();
        var printer = await QuietPrinterAsync(PrinterActivity.Idle, Since);
        var task = await CreateTaskAsync(admin, new { everyDays = 30, printerIds = new[] { printer } });
        var enrollment = (await EnrollmentsAsync(admin, $"taskId={task.Id}")).Single();
        var viewer = await ViewerAsync(admin);

        (await viewer.GetAsync("/api/v1/maintenance/tasks")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/v1/maintenance/enrollments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/v1/maintenance/logs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync("/api/v1/maintenance/tasks", new { name = "Nope", everyDays = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/v1/maintenance/enrollments/complete", new { enrollmentIds = new[] { enrollment.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/v1/maintenance/tasks/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await EnrollInTasksAsync(viewer, printer, [task.Id])).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<MaintenanceTaskResponse> CreateTaskAsync(HttpClient client, object fields)
    {
        var body = JsonSerializer.SerializeToNode(fields, JsonSerializerOptions.Web)!.AsObject();
        body.TryAdd("name", $"Task {Guid.NewGuid():N}");
        var response = await client.PostAsJsonAsync("/api/v1/maintenance/tasks", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MaintenanceTaskResponse>())!;
    }

    private static async Task<IReadOnlyList<MaintenanceEnrollmentResponse>> EnrollmentsAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<MaintenanceEnrollmentsResponse>($"/api/v1/maintenance/enrollments?{query}"))!.Items;

    private static Task<HttpResponseMessage> EnrollInTasksAsync(HttpClient client, Guid printerId, Guid[] taskIds) =>
        client.PostAsJsonAsync($"/api/v1/printers/{printerId}/maintenance/enrollments", new { taskIds });

    private async Task<MaintenanceMeasuredUsage> UsageAsync(Guid printerId, DateTimeOffset since, DateTimeOffset until)
    {
        using var scope = Factory.Services.CreateScope();
        var id = Guid.NewGuid();
        var usage = await scope.ServiceProvider.GetRequiredService<IMaintenanceUsageReadModel>()
            .ReadAsync([new MaintenanceUsageWindow(id, printerId, since, until)], default);
        return usage[id];
    }

    /// <summary>A printer with no closed spans whose open stretch is <paramref name="activity"/> from <paramref name="sinceUtc"/>.</summary>
    private async Task<Guid> QuietPrinterAsync(PrinterActivity activity, DateTimeOffset sinceUtc)
    {
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var name = activity.ToString();
        await db.Database.ExecuteSqlAsync($"""DELETE FROM "PrinterActivitySpans" WHERE "PrinterId" = {printer.Id}""");
        await db.Database.ExecuteSqlAsync(
            $"""UPDATE "Printers" SET "Activity" = {name}, "ActivitySinceUtc" = {sinceUtc.ToUniversalTime()} WHERE "Id" = {printer.Id}""");
        return printer.Id;
    }

    private async Task SpanAsync(Guid printerId, PrinterActivity activity, DateTimeOffset from, DateTimeOffset to)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var name = activity.ToString();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "PrinterActivitySpans" ("Id", "PrinterId", "Activity", "StartedAtUtc", "EndedAtUtc")
            VALUES ({Guid.NewGuid()}, {printerId}, {name}, {from.ToUniversalTime()}, {to.ToUniversalTime()})
            """);
    }

    /// <summary>A completed printer-started print with no activity spans, as when its end was inferred.</summary>
    private async Task EndedPrintAsync(Guid printerId, DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        using var scope = Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExternalPrintRepository>();
        var print = ExternalPrint.Start(printerId, "benchy", []);
        await repository.AddAsync(print, default);
        await repository.SaveChangesAsync(default);
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "Prints" SET "Status" = 'Completed', "StartedAtUtc" = {startedAt.ToUniversalTime()}, "EndedAtUtc" = {endedAt.ToUniversalTime()}
            WHERE "Id" = {print.Id}
            """);
    }

    private async Task<string> PrinterNameAsync(Guid printerId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Printers.Where(p => p.Id == printerId).Select(p => p.Name).SingleAsync();
    }

    private async Task<HttpClient> ViewerAsync(HttpClient admin)
    {
        var groups = (await admin.GetFromJsonAsync<GroupResponse[]>("/api/v1/admin/groups"))!;
        var username = $"viewer-{Guid.NewGuid():N}"[..20];
        var create = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new AccountInput(username, $"{username}@example.test", null, [groups.Single(g => g.Name == "Viewers").Id], [], true, "temporary"));
        create.EnsureSuccessStatusCode();
        var userId = (await create.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Auth.EntityIdResponse>())!.Id;
        using var scope = Factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<BrowserSessionService>().CreateAsync(userId, "Test", default);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        return client;
    }
}
