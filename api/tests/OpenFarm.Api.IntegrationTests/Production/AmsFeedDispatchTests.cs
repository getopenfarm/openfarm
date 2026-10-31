using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Api.Hubs;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>Records what dispatch tells the printer to feed, instead of talking to one.</summary>
public sealed class FeedMappingRecorder : IPrinterConnectorFactory
{
    public ConcurrentQueue<(string SerialNumber, int Plate, FilamentFeedMapping? Mapping)> Started { get; } = [];

    /// <summary>Every send tried, failed ones included, by printer serial number.</summary>
    public ConcurrentQueue<string> SendAttempts { get; } = [];

    /// <summary>Runs before each send is recorded, with the printer serial number; throwing fails that send, standing in for a printer that does not answer.</summary>
    public Func<string, Task>? BeforeSend { get; set; }

    /// <summary>Runs after the connector reported <see cref="PrintSendStage.Starting"/>; throwing fails the send once the start command may have left.</summary>
    public Func<string, Task>? AfterStart { get; set; }

    /// <summary>The send progress dispatch pushed to the hub, per job.</summary>
    public ConcurrentQueue<(Guid JobId, PrintSendStage Stage, int? Percent, int Attempt)> SendProgress { get; } = [];

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

        public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) =>
            StartJobAsync(details, fileContent, jobName, plate, feedMapping, null, cancellationToken);

        public async Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping,
            IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken)
        {
            owner.SendAttempts.Enqueue(details.SerialNumber);
            progress?.Report(new PrintSendProgress(PrintSendStage.Uploading, 50));
            if (owner.BeforeSend is { } beforeSend) await beforeSend(details.SerialNumber);
            progress?.Report(new PrintSendProgress(PrintSendStage.Uploading, 100));
            progress?.Report(new PrintSendProgress(PrintSendStage.Starting));
            if (owner.AfterStart is { } afterStart) await afterStart(details.SerialNumber);
            owner.Started.Enqueue((details.SerialNumber, plate, feedMapping));
        }

        public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>The hub publisher, also noting send progress for the test to read.</summary>
public sealed class SendProgressRecorder(IProductionEventPublisher inner, FeedMappingRecorder recorder) : IProductionEventPublisher
{
    public Task ProductionRunStatusChangedAsync(Guid productionRunId, ProductionRunStatus status, CancellationToken cancellationToken) =>
        inner.ProductionRunStatusChangedAsync(productionRunId, status, cancellationToken);

    public Task PrintJobStatusChangedAsync(Guid printJobId, PrintJobStatus status, CancellationToken cancellationToken) =>
        inner.PrintJobStatusChangedAsync(printJobId, status, cancellationToken);

    public Task PrintJobDispatchedAsync(Guid printJobId, Guid printerId, CancellationToken cancellationToken) =>
        inner.PrintJobDispatchedAsync(printJobId, printerId, cancellationToken);

    public Task NotificationsChangedAsync(CancellationToken cancellationToken) => inner.NotificationsChangedAsync(cancellationToken);

    public Task PrintJobSendProgressAsync(Guid printJobId, PrintSendStage stage, int? percent, int attempt, string? failure, CancellationToken cancellationToken)
    {
        recorder.SendProgress.Enqueue((printJobId, stage, percent, attempt));
        return inner.PrintJobSendProgressAsync(printJobId, stage, percent, attempt, failure, cancellationToken);
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
            services.RemoveAll<IProductionEventPublisher>();
            services.AddScoped<IProductionEventPublisher>(sp =>
                new SendProgressRecorder(ActivatorUtilities.CreateInstance<FarmHubProductionEventPublisher>(sp), Recorder));
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

    private async Task<PrintJob> WaitForJobAsync(Guid runId, Func<PrintJob, bool> settled)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        PrintJob job;
        do
        {
            await Task.Delay(100);
            job = (await JobsAsync(runId)).Single();
        }
        while (!settled(job) && timer.Elapsed < TimeSpan.FromSeconds(10));
        return job;
    }

    private async Task<PrintJob> DispatchWithSendAsync(Guid runId, Func<string, Task> beforeSend, Func<PrintJob, bool> settled)
    {
        Recorder.BeforeSend = beforeSend;
        using var worker = Worker();
        await worker.StartAsync(default);
        try { return await WaitForJobAsync(runId, settled); }
        finally
        {
            await worker.StopAsync(default);
            Recorder.BeforeSend = null;
            Recorder.AfterStart = null;
        }
    }

    private int SendAttemptsTo(Printer printer) => Recorder.SendAttempts.Count(serial => serial == printer.SerialNumber);

    [Fact]
    public async Task SendTimingOut_EveryAttempt_FailsTheJobAndFreesThePrinter()
    {
        // A printer that never answers the MQTT connect: the connector's own deadline cancels it, the host is still running.
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var runId = (await RunAsync(client, part.Id, pinned: printer.Id)).Id;

        var job = await DispatchWithSendAsync(runId, _ => throw new OperationCanceledException("MQTT connect canceled."),
            j => j.Status == PrintJobStatus.Failed);

        job.Status.Should().Be(PrintJobStatus.Failed);
        job.PrinterId.Should().Be(printer.Id, "the failed job keeps the printer it was sent to");
        job.LastBlockReason.Should().Be("Send failed: MQTT connect canceled.");
        (await ActiveJobOnAsync(printer.Id)).Should().BeNull("a failed send must not hold the printer");
        SendAttemptsTo(printer).Should().Be(3);
        Recorder.SendProgress.Where(p => p.JobId == job.Id).Select(p => p.Stage).Should()
            .ContainInOrder(PrintSendStage.Slicing, PrintSendStage.Retrying, PrintSendStage.Retrying, PrintSendStage.Ended);
    }

    [Fact]
    public async Task SendFailingOnce_IsRetriedInPlaceAndSent()
    {
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var runId = (await RunAsync(client, part.Id, pinned: printer.Id)).Id;
        var failures = 1;

        var job = await DispatchWithSendAsync(runId,
            serial => serial == printer.SerialNumber && failures-- > 0 ? throw new IOException("Upload connection failed") : Task.CompletedTask,
            // The printer is assigned before the send; wait for the send itself.
            j => Recorder.SendProgress.Any(p => p.JobId == j.Id && p.Stage == PrintSendStage.Sent));

        job.PrinterId.Should().Be(printer.Id);
        job.LastBlockReason.Should().BeNull();
        SendAttemptsTo(printer).Should().Be(2);
        SentTo(printer).Should().ContainSingle();
        Recorder.SendProgress.Where(p => p.JobId == job.Id).Select(p => (p.Stage, p.Percent, p.Attempt)).Should().Equal(
            (PrintSendStage.Slicing, null, 1),
            (PrintSendStage.Uploading, 50, 1),
            (PrintSendStage.Retrying, null, 2),
            (PrintSendStage.Uploading, 50, 2),
            (PrintSendStage.Uploading, 100, 2),
            (PrintSendStage.Starting, null, 2),
            (PrintSendStage.Sent, null, 1));
    }

    [Fact]
    public async Task SendFailing_WhilePrinterStartsPrinting_IsNotSentAgain()
    {
        // The start command may have landed before the call failed: a printer that is now printing must not get it twice.
        var client = await ClientAsync();
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var runId = (await RunAsync(client, part.Id, pinned: printer.Id)).Id;

        var job = await DispatchWithSendAsync(runId, async serial =>
        {
            if (serial != printer.SerialNumber) return;
            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
                (await db.Printers.SingleAsync(p => p.Id == printer.Id)).ObserveStatus("RUNNING");
                await db.SaveChangesAsync();
            }

            throw new IOException("Connection reset");
        }, j => j.LastBlockReason is not null);

        SendAttemptsTo(printer).Should().Be(1);
        job.LastBlockReason.Should().Be("Send failed: Connection reset");
        job.Status.Should().Be(PrintJobStatus.Failed, "a job that may be printing is never queued to be sent again");
    }

    [Fact]
    public async Task ExactQuantityJob_UploadFailingEveryAttempt_IsRetriedThenFailsInsteadOfStayingDispatched()
    {
        // The reported case: the plate was marked "start requested" before the upload, so an upload that never finished held
        // the printer as "Dispatching…" forever, restart included.
        var client = await ClientAsync();
        var part = await MappedPartAsync(1);
        var printer = await PrinterAsync();
        var runId = (await RunAsync(client, part.Id, pinned: printer.Id)).Id;
        (await JobsAsync(runId)).Single().ObjectExecution.Should().NotBeNull();

        var job = await DispatchWithSendAsync(runId, _ => throw new IOException("Printer file upload did not succeed (Failed)."),
            j => j.Status == PrintJobStatus.Failed);

        job.Status.Should().Be(PrintJobStatus.Failed);
        job.ObjectExecution!.StartRequested.Should().BeFalse("the start command never left");
        SendAttemptsTo(printer).Should().Be(3);
        (await ActiveJobOnAsync(printer.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ExactQuantityJob_StartUnconfirmed_IsNotResent_AndFailsOnceTheIdlePrinterShowsItNeverStarted()
    {
        var client = await ClientAsync();
        var part = await MappedPartAsync(1);
        var printer = await PrinterAsync();
        var runId = (await RunAsync(client, part.Id, pinned: printer.Id)).Id;
        Recorder.AfterStart = _ => throw new IOException("Connection reset");
        using var worker = Worker(startConfirmGrace: TimeSpan.Zero);
        await worker.StartAsync(default);
        try
        {
            var job = await WaitForJobAsync(runId, j => j.LastBlockReason is not null);
            job.Status.Should().Be(PrintJobStatus.Queued);
            job.PrinterId.Should().Be(printer.Id);
            job.ObjectExecution!.StartRequested.Should().BeTrue();
            SendAttemptsTo(printer).Should().Be(1, "a start that may have landed is never sent twice");

            await ReportIdleAsync(printer.Id);
            worker.RequestDispatchAttempt(printer.Id);

            job = await WaitForJobAsync(runId, j => j.Status == PrintJobStatus.Failed);
            job.Status.Should().Be(PrintJobStatus.Failed);
            job.LastBlockReason.Should().Be("Send failed: the printer did not start the print.");
            SendAttemptsTo(printer).Should().Be(1);
        }
        finally
        {
            await worker.StopAsync(default);
            Recorder.AfterStart = null;
        }
    }

    private async Task<PrintJob?> ActiveJobOnAsync(Guid printerId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPrintJobRepository>().GetActiveJobByDispatchedPrinterIdAsync(printerId, default);
    }
}
