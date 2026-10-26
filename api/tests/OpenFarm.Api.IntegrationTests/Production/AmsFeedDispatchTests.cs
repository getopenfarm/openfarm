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
    public ConcurrentQueue<(string SerialNumber, int Plate, FilamentFeedMapping? Mapping)> Started { get; } = [];

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

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken)
        {
            owner.Started.Enqueue((details.SerialNumber, plate, feedMapping));
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
    private async Task AttachAmsAsync(Guid printerId, int? moveSpoolToSlot, bool externalFeeding)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var ams = FeedUnit.CreateConnectorDerived(printerId, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var slots = Enumerable.Range(0, 4).Select(position => ams.AddFeedSlot(position)).ToArray();
        db.FeedUnits.Add(ams);
        if (moveSpoolToSlot is { } slot)
        {
            var spool = await db.Spools.SingleAsync(s => s.State == SpoolState.Using && db.FeedUnits.Any(u => u.PrinterId == printerId && u.FeedSlots.Any(f => f.Id == s.FeedSlotId)));
            spool.ReturnToStorage();
            spool.LoadOntoFeedSlot(slots[slot].Id);
        }

        (await db.Printers.SingleAsync(p => p.Id == printerId)).RecordTelemetry(null, null, null, null, null, null, [], null, null,
            [externalFeeding ? new FeedSlotRef(FeedUnitKindCatalog.ExternalSpool, 0, 0) : null]);
        await db.SaveChangesAsync();
    }

    private List<FilamentFeedMapping?> SentTo(Printer printer) =>
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
    public async Task AnExportedSinglePlate_IsSentAsThatPlateWithItsOwnGrams()
    {
        // Bambu Studio / Orca "export plate sliced file" of plate 4: only plate 4 has G-code, and slice_info describes plate 4.
        using var project = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(project, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var gcode = new StreamWriter(archive.CreateEntry("Metadata/plate_4.gcode").Open())) gcode.Write("G28\n");
            using var sliceInfo = new StreamWriter(archive.CreateEntry("Metadata/slice_info.config").Open());
            sliceInfo.Write("""<config><plate><metadata key="index" value="4"/><filament id="1" type="PLA" color="#000000" used_g="5.5"/></plate></config>""");
        }

        var client = await ClientAsync();
        var part = await PartAsync(fileName: "part.gcode.3mf", file: project.ToArray());
        var printer = await PrinterAsync();
        var runId = (await RunAsync(client, part.Id, pinned: printer.Id)).Id;

        using (var worker = Worker())
        {
            await worker.StartAsync(default);
            try { await WaitForAssignmentAsync(runId); }
            finally { await worker.StopAsync(default); }
        }

        Recorder.Started.Should().ContainSingle(s => s.SerialNumber == printer.SerialNumber).Which.Plate.Should().Be(4);
        (await JobsAsync(runId)).Single().Filaments.Single().EstimatedGrams.Should().Be(5.5m);
    }

    [Fact]
    public async Task SingleColourSpoolInAnAmsSlot_IsDispatchedThroughTheAms()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: 2, externalFeeding: false);

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.Tools.Should().Equal(new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 2));
    }

    [Fact]
    public async Task SnapmakerU1_SpoolOnAToolhead_IsDispatchedToThatHead()
    {
        var client = await ClientAsync();
        var part = await PartAsync(contents: "; printer_model = Snapmaker U1\nG28\n");
        var printer = await PrinterAsync(brand: "Snapmaker", modelCode: "U1");
        // The U1's four heads are one four-slot unit; slot 2 is T3.
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: 2, externalFeeding: false);

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        // The connector sends this to the firmware as map_table [[0, 2]].
        mapping!.Tools.Should().Equal(new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 2));
    }

    [Fact]
    public async Task SnapmakerU1_MixedNozzles_SendsTheJobToAHeadWithItsNozzle()
    {
        var client = await ClientAsync();
        var part = await PartAsync(contents: "; printer_model = Snapmaker U1\nG28\n");
        var printer = await PrinterAsync(brand: "Snapmaker", modelCode: "U1");
        // The job's filament is on T1 with a 0.6 mm nozzle and on T3 with the job's 0.4 mm.
        var slots = await AttachHeadsAsync(printer.Id, [0.6m, 0.4m, 0.4m, 0.4m], head: 0);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var onT1 = await db.Spools.SingleAsync(s => s.FeedSlotId == slots[0].Id);
            var twin = Spool.Create(onT1.FilamentVariantId, null, 1000, 1000);
            twin.LoadOntoFeedSlot(slots[2].Id);
            db.Spools.Add(twin);
            await db.SaveChangesAsync();
        }

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.Tools.Should().Equal(new FeedSlotRef(FeedUnitKindCatalog.MultiMaterialUnit, 0, 2));
    }

    [Fact]
    public async Task SnapmakerU1_FileSlicedForAnotherModel_IsNotSent()
    {
        var client = await ClientAsync();
        var part = await PartAsync(contents: "; printer_model = Bambu Lab P1S\nG28\n");
        var printer = await PrinterAsync(brand: "Snapmaker", modelCode: "U1");
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: 2, externalFeeding: false);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try { await Task.Delay(1500); }
        finally { await worker.StopAsync(default); }

        (await JobsAsync(run.Id)).Single().PrinterId.Should().BeNull();
        SentTo(printer).Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalSpoolLoadedIntoTheExtruderOfAnAmsPrinter_IsDispatchedAsExternal()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: null, externalFeeding: true);

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.Tools.Should().Equal(new FeedSlotRef(FeedUnitKindCatalog.ExternalSpool, 0, 0));
    }

    [Fact]
    public async Task ExternalSpoolNotLoadedIntoTheExtruderOfAnAmsPrinter_IsNotUsed()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        await AttachAmsAsync(printer.Id, moveSpoolToSlot: null, externalFeeding: false);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try { await Task.Delay(1500); }
        finally { await worker.StopAsync(default); }

        (await JobsAsync(run.Id)).Single().PrinterId.Should().BeNull();
        SentTo(printer).Should().BeEmpty();
    }

    [Fact]
    public async Task PrinterWithoutAms_StillDispatchesFromTheExternalSpool()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();

        var mapping = await DispatchedMappingAsync(printer, async () => (await RunAsync(client, part.Id, pinned: printer.Id)).Id);

        mapping!.Tools.Should().OnlyContain(slot => slot == null || !slot.Value.IsMultiMaterialUnit);
    }
}
