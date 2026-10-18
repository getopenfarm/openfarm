using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Notifications;
using OpenFarm.Api.Endpoints.Prints;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>Closed prints whose filament deduction was skipped surface as action-required items, and Assign spool settles them.</summary>
public class NotificationsTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    /// <summary>A one-tool external print on a printer with a loaded spool; <paramref name="observeTray"/> binds that spool at the end.</summary>
    private async Task<(Guid PrintId, Guid PrinterId, Guid SpoolId)> StartExternalAsync(bool observeTray)
    {
        await ClientAsync();
        var printer = await PrinterAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var spool = await LoadedSpoolAsync(db, printer.Id);
        var print = ExternalPrint.Start(printer.Id, "benchy", [new ExternalFilamentSpec(0, "PLA", "000000", 20m, null)]);
        if (observeTray) print.ObserveTray(0, spool.Id);
        var repository = scope.ServiceProvider.GetRequiredService<IExternalPrintRepository>();
        await repository.AddAsync(print, default);
        await repository.SaveChangesAsync(default);
        return (print.Id, printer.Id, spool.Id);
    }

    private static async Task<Spool> LoadedSpoolAsync(OpenFarmDbContext db, Guid printerId)
    {
        var slotIds = await db.FeedUnits.Where(u => u.PrinterId == printerId).SelectMany(u => u.FeedSlots).Select(s => s.Id).ToListAsync();
        return await db.Spools.SingleAsync(s => s.FeedSlotId != null && slotIds.Contains(s.FeedSlotId.Value));
    }

    private async Task SetProgressAsync(Guid printerId, int percent)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await db.Printers.SingleAsync(p => p.Id == printerId)).RecordTelemetry(null, null, percent, null, null, null, [], null, null, null);
        await db.SaveChangesAsync();
    }

    private async Task<decimal> RemainingAsync(Guid spoolId)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Spools.SingleAsync(s => s.Id == spoolId)).RemainingWeightGrams;
    }

    private static async Task<List<NotificationItemResponse>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListNotificationsResponse>("/api/v1/notifications"))!.Items;

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, NotificationItemResponse item, Guid spoolId) =>
        client.PostAsJsonAsync($"/api/v1/prints/{item.PrintId}/filament-lines/{item.Lines[0].LineId}/spool", new { spoolId });

    [Fact]
    public async Task AnUnboundExternalPrint_ShowsUpAndAssigningASpoolDeductsItOnce()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(observeTray: false);
        await ReplayAsync(printerId, "FINISH");
        var client = await ClientAsync();

        var item = (await ListAsync(client)).Single(i => i.PrintId == printId);
        item.Type.Should().Be("FilamentNotDeducted");
        item.Source.Should().Be("External");
        item.Status.Should().Be("Completed");
        item.PrinterId.Should().Be(printerId);
        item.Lines.Should().ContainSingle(l => l.Reason == "NoSpool" && l.EstimatedGrams == 20m && l.MaterialType == "PLA");

        var response = await AssignAsync(client, item, spoolId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<AssignPrintFilamentSpoolResponse>())!.GramsDeducted.Should().Be(20m);

        (await RemainingAsync(spoolId)).Should().Be(980m);
        (await ListAsync(client)).Should().NotContain(i => i.PrintId == printId);
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().SpoolUsageRecords.Where(r => r.SpoolId == spoolId).ToListAsync())
            .Should().ContainSingle(r => r.Source == SpoolUsageSource.Job && r.JobId == printId && r.GramsConsumed == 20m);

        (await AssignAsync(client, item, spoolId)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the line is settled");
        (await RemainingAsync(spoolId)).Should().Be(980m);
    }

    [Fact]
    public async Task AFailedPrint_DeductsByTheProgressStoredWhenItClosed()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(observeTray: false);
        await SetProgressAsync(printerId, 50);
        await ReplayAsync(printerId, "FAILED");
        await SetProgressAsync(printerId, 100); // later telemetry must not change what the print stopped at
        var client = await ClientAsync();

        var item = (await ListAsync(client)).Single(i => i.PrintId == printId);
        (await AssignAsync(client, item, spoolId)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RemainingAsync(spoolId)).Should().Be(990m);
    }

    [Fact]
    public async Task AFailedPrintWithNoProgress_CannotBeAssigned()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(observeTray: false);
        await ReplayAsync(printerId, "FAILED");
        var client = await ClientAsync();

        var item = (await ListAsync(client)).Single(i => i.PrintId == printId);
        item.Lines.Should().ContainSingle(l => l.Reason == "NoProgress");
        (await AssignAsync(client, item, spoolId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RemainingAsync(spoolId)).Should().Be(1000m);
    }

    [Fact]
    public async Task ASpoolThatWasUnloaded_IsReportedAndOnlyALoadedSpoolIsAccepted()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(observeTray: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Spools.SingleAsync(s => s.Id == spoolId)).ReturnToStorage();
            await db.SaveChangesAsync();
        }

        await ReplayAsync(printerId, "FINISH");
        var client = await ClientAsync();

        var item = (await ListAsync(client)).Single(i => i.PrintId == printId);
        item.Lines.Should().ContainSingle(l => l.Reason == "SpoolNotLoaded" && l.SpoolId == spoolId);
        (await AssignAsync(client, item, spoolId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RemainingAsync(spoolId)).Should().Be(1000m);
    }

    [Fact]
    public async Task ADeductedPrint_IsNotListed()
    {
        var (printId, printerId, _) = await StartExternalAsync(observeTray: true);
        await ReplayAsync(printerId, "FINISH");

        (await ListAsync(await ClientAsync())).Should().NotContain(i => i.PrintId == printId);
    }

    [Fact]
    public async Task Dismissing_HidesTheItemAndLeavesTheSpoolAlone()
    {
        var (printId, printerId, spoolId) = await StartExternalAsync(observeTray: false);
        await ReplayAsync(printerId, "FINISH");
        var client = await ClientAsync();

        (await client.PostAsJsonAsync($"/api/v1/notifications/{printId}/dismiss", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListAsync(client)).Should().NotContain(i => i.PrintId == printId);
        (await RemainingAsync(spoolId)).Should().Be(1000m);
    }

    [Fact]
    public async Task ADispatchedJobThatSkipped_AppearsToo_WithThePartName()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var run = await RunAsync(client, part.Id);
        var job = (await JobsAsync(run.Id)).Single();
        await AssignAsync(job.Id, printer.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPrintJobRepository>();
            (await repository.GetByIdAsync(job.Id, default))!.BindFilamentUsage([(null, 20m, null)]);
            await repository.SaveChangesAsync(default);
        }

        await ReplayAsync(printer.Id, "FINISH");

        var item = (await ListAsync(client)).Single(i => i.PrintId == job.Id);
        item.Source.Should().Be("Dispatched");
        item.Name.Should().Be(part.Name);
        item.Lines.Should().ContainSingle(l => l.Reason == "NoSpool");
    }

    [Fact]
    public async Task AnUnknownPrint_IsNotFound()
    {
        var client = await ClientAsync();

        (await client.PostAsJsonAsync($"/api/v1/notifications/{Guid.NewGuid()}/dismiss", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
