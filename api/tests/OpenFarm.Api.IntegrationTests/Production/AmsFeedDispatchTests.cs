using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>Records what dispatch tells the printer to feed, instead of talking to one.</summary>
public sealed class FeedMappingRecorder : IPrinterConnectorFactory
{
    public ConcurrentQueue<(string SerialNumber, FilamentFeedMapping? Mapping)> Started { get; } = [];

    public IPrinterConnector CreateConnector(PrinterConnectionDetails details) => new Connector(this);

    public IPrinterCameraRelay CreateCameraRelay(PrinterConnectionDetails details) => throw new NotSupportedException();

    private sealed class Connector(FeedMappingRecorder owner) : IPrinterConnector
    {
        public Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.FromResult(true);

        public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(PrinterConnectionDetails details, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken)
        {
            owner.Started.Enqueue((details.SerialNumber, feedMapping));
            return Task.CompletedTask;
        }

        public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

public sealed class AmsFeedFactory : ProductionTestFactory
{
    public FeedMappingRecorder Recorder { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrinterConnectorFactory>();
            services.AddSingleton<IPrinterConnectorFactory>(Recorder);
        });
    }
}

public class AmsFeedDispatchTests(AmsFeedFactory factory) : ProductionTestContext(factory), IClassFixture<AmsFeedFactory>
{
    private FeedMappingRecorder Recorder => ((AmsFeedFactory)Factory).Recorder;

    /// <summary>Gives the printer an AMS and moves its one loaded spool from the external port into AMS slot <paramref name="slot"/>.</summary>
    private async Task AttachAmsAsync(Guid printerId, int? moveSpoolToSlot, int? activeTrayId)
    {
        using var scope = Factory.CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var ams = FeedUnit.CreateConnectorDerived(OpenFarm.Domain.Organizations.Organization.WellKnownId, printerId, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = Enumerable.Range(0, 4).Select(ams.AddFeedSlot).ToArray();
        db.FeedUnits.Add(ams);
        if (moveSpoolToSlot is { } slot)
        {
            var spool = await db.Spools.SingleAsync(s => s.State == SpoolState.Using && db.FeedUnits.Any(u => u.PrinterId == printerId && u.FeedSlots.Any(f => f.Id == s.FeedSlotId)));
            spool.ReturnToStorage();
            spool.LoadOntoFeedSlot(slots[slot].Id);
        }

        (await db.Printers.SingleAsync(p => p.Id == printerId)).RecordTelemetry(null, null, null, null, null, null, [], null, null, null, activeTrayId);
        await db.SaveChangesAsync();
    }

    private IReadOnlyList<FilamentFeedMapping?> SentTo(Printer printer) =>
        Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Select(s => s.Mapping).ToList();

    private async Task<FilamentFeedMapping?> DispatchedMappingAsync(Printer printer, Func<Task<Guid>> setup)
    {
        var runId = await setup();
        using var worker = Worker();
        await worker.StartAsync(default);
        try { await WaitForAssignmentAsync(runId); }
        finally { await worker.StopAsync(default); }
        return SentTo(printer).Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task SingleColourSpoolInAnAmsSlot_IsDispatchedThroughTheAms()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: 2, activeTrayId: 255);

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.UseAms.Should().BeTrue();
        mapping.Trays.Should().Equal(2);
    }

    [Fact]
    public async Task ExternalSpoolLoadedIntoTheExtruderOfAnAmsPrinter_IsDispatchedAsExternal()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: null, activeTrayId: 254);

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.UseAms.Should().BeFalse();
        mapping.Trays.Should().Equal(-1);
    }

    [Fact]
    public async Task ExternalSpoolNotLoadedIntoTheExtruderOfAnAmsPrinter_IsNotUsed()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: null, activeTrayId: 255);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try { await Task.Delay(1500); }
        finally { await worker.StopAsync(default); }

        (await JobsAsync(run.Id)).Single().DispatchedPrinterId.Should().BeNull();
        SentTo(printer).Should().BeEmpty();
    }

    [Fact]
    public async Task PrinterWithoutAms_StillDispatchesFromTheExternalSpool()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.UseAms.Should().BeFalse();
    }
}
