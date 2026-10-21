using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Production;

/// <summary>Stands in for the bundled slicers (Orca 2.3.1/2.4.2, Bambu Studio 2.7.1) on the host platform; entries can be removed or added to simulate a bundle change.</summary>
public sealed class SlicerBundleDouble(SlicerPlatform hostPlatform) : IAvailableSlicerCatalog
{
    public static readonly SlicerRef Orca231 = new(SlicerEngine.OrcaSlicer, "2.3.1");
    public static readonly SlicerRef Orca242 = new(SlicerEngine.OrcaSlicer, "2.4.2");
    public static readonly SlicerRef Bambu271 = new(SlicerEngine.BambuStudio, "2.7.1");

    private readonly object _gate = new();
    private readonly List<AvailableSlicer> _available = [For(hostPlatform, Orca231), For(hostPlatform, Orca242), For(hostPlatform, Bambu271)];

    private static AvailableSlicer For(SlicerPlatform platform, SlicerRef slicer) => new(slicer.Engine, slicer.Version, platform, $"/bundle/{slicer.Engine}/{slicer.Version}");

    public IReadOnlyList<AvailableSlicer> GetAvailable()
    {
        lock (_gate) return _available.ToList();
    }

    public AvailableSlicer? Find(SlicerRef slicerRef, SlicerPlatform platform) =>
        GetAvailable().FirstOrDefault(a => a.ToSlicerRef() == slicerRef && a.Platform == platform);

    public void Remove(SlicerRef slicer)
    {
        lock (_gate) _available.RemoveAll(a => a.ToSlicerRef() == slicer);
    }

    public void Add(SlicerRef slicer)
    {
        lock (_gate)
        {
            if (!_available.Any(a => a.ToSlicerRef() == slicer)) _available.Add(For(hostPlatform, slicer));
        }
    }
}

/// <summary>Records what production asks the slicer to do instead of running one.</summary>
public sealed class SpySlicerEngine : ISlicerEngine
{
    public ConcurrentQueue<(Guid PrinterId, SlicerRef Slicer, ResolvedSlicingProfiles Profiles)> Calls { get; } = [];

    // Scoped to one printer: jobs other scenarios left queued may be dispatched by this scenario's worker, and a hook
    // or failure meant for one printer must never act on another's.
    private readonly ConcurrentDictionary<Guid, Func<ResolvedSlicingProfiles, Task>> _whileSlicing = [];
    private readonly ConcurrentDictionary<Guid, Exception> _failures = [];

    /// <summary>Runs while this printer's "slice" is in progress — the window in which the farm can change under a file being made.</summary>
    public void WhileSlicing(Guid printerId, Func<ResolvedSlicingProfiles, Task> hook) => _whileSlicing[printerId] = hook;

    public void FailSlicing(Guid printerId, Exception failure) => _failures[printerId] = failure;

    public void Heal(Guid printerId)
    {
        _whileSlicing.TryRemove(printerId, out _);
        _failures.TryRemove(printerId, out _);
    }

    public List<(SlicerRef Slicer, ResolvedSlicingProfiles Profiles)> CallsFor(Guid printerId) =>
        Calls.Where(c => c.PrinterId == printerId).Select(c => (c.Slicer, c.Profiles)).ToList();

    public async Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        string materialType, decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken)
    {
        Calls.Enqueue((printerId, slicer, profiles));
        if (_whileSlicing.TryGetValue(printerId, out var hook)) await hook(profiles);
        if (_failures.TryGetValue(printerId, out var failure)) throw failure;
        return new SlicingResult(1, 42, 12m, [], Encoding.UTF8.GetBytes("; fake sliced gcode\n"));
    }
}

public sealed class StrictSlicingFactory : ProductionTestFactory
{
    public FeedMappingRecorder Recorder { get; } = new();

    public SpySlicerEngine Spy { get; } = new();

    public SlicerBundleDouble Bundle { get; private set; } = null!;

    /// <summary>One login for the whole class: the login endpoint throttles, and a dozen scenarios would trip it.</summary>
    public string? Token { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPrinterConnectorFactory>();
            services.AddSingleton<IPrinterConnectorFactory>(Recorder);
            services.RemoveAll<ISlicerEngine>();
            services.AddSingleton<ISlicerEngine>(Spy);
            services.RemoveAll<IAvailableSlicerCatalog>();
            services.AddSingleton<IAvailableSlicerCatalog>(sp => Bundle = new SlicerBundleDouble(sp.GetRequiredService<IHostPlatformProvider>().Current));
        });
    }
}

// "Choose 0.20mm Standard once; each part is sliced with its own pinned slicer's copy of it — or not at all."
// Every scenario runs the real dispatcher over real Postgres rows and asserts what reached the printer: when the
// pinned slicer cannot produce the file from the presets the operator selected, NOTHING is sent and the operator is told.
public class StrictSlicingDispatchTests(StrictSlicingFactory factory) : ProductionTestContext(factory), IClassFixture<StrictSlicingFactory>
{
    private static readonly SlicerRef Orca231 = SlicerBundleDouble.Orca231;
    private static readonly SlicerRef Orca242 = SlicerBundleDouble.Orca242;
    private static readonly SlicerRef Bambu271 = SlicerBundleDouble.Bambu271;
    private const string MachineName = "Bambu Lab P1S 0.4 nozzle";
    private const string ProcessName = "0.20mm Standard @BBL X1C";
    private const string FilamentName = "Bambu PLA Basic @BBL X1C";

    private StrictSlicingFactory Strict => (StrictSlicingFactory)Factory;

    private new async Task<HttpClient> ClientAsync()
    {
        if (Strict.Token is null)
        {
            var first = await base.ClientAsync();
            Strict.Token = first.DefaultRequestHeaders.Authorization!.Parameter;
            return first;
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Strict.Token);
        return client;
    }
    private SpySlicerEngine Spy => Strict.Spy;
    private FeedMappingRecorder Recorder => Strict.Recorder;

    private sealed record Presets(
        IReadOnlyDictionary<SlicerRef, MachineProfile> Machine,
        IReadOnlyDictionary<SlicerRef, ProcessProfile> Process,
        IReadOnlyDictionary<SlicerRef, FilamentProfile> Filament);

    private static string Marker(string kind, SlicerRef catalog) => $"{kind}|{catalog.Engine}-{catalog.Version}";

    private static ProfileSourceFormat FormatOf(SlicerRef catalog) => ProfileEngineCompatibility.NativeFormat(catalog.Engine)!.Value;

    // The same three logical presets (ids GM014 / GP004 / GFSA00, one vendor) in each given catalog, every row's JSON
    // carrying a marker naming its catalog. Catalogs not listed in <paramref name="without"/> for a kind get that kind.
    private async Task<Presets> SeedAsync(string vendor, SlicerRef[] catalogs, SlicerRef[]? withoutProcess = null, SlicerRef[]? withoutFilament = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var machines = new Dictionary<SlicerRef, MachineProfile>();
        var processes = new Dictionary<SlicerRef, ProcessProfile>();
        var filaments = new Dictionary<SlicerRef, FilamentProfile>();
        foreach (var catalog in catalogs)
        {
            var machine = MachineProfile.CreateSystemProfile(MachineName, FormatOf(catalog), null,
                $$"""{"type":"machine","nozzle_diameter":["0.4"],"marker":"{{Marker("MACHINE", catalog)}}"}""",
                vendor, "GM014", isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: catalog.Version);
            db.MachineProfiles.Add(machine);
            machines[catalog] = machine;
            if (withoutProcess is null || !withoutProcess.Contains(catalog))
            {
                var process = ProcessProfile.CreateSystemProfile(ProcessName, FormatOf(catalog), null,
                    $$"""{"type":"process","layer_height":"0.2","marker":"{{Marker("PROCESS", catalog)}}"}""",
                    vendor, "GP004", isSelectable: true, compatibleMachineNames: [MachineName], sourceVersion: catalog.Version);
                db.ProcessProfiles.Add(process);
                processes[catalog] = process;
            }

            if (withoutFilament is null || !withoutFilament.Contains(catalog))
            {
                var filament = FilamentProfile.CreateSystemProfile(FilamentName, FormatOf(catalog), null,
                    $$"""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"compatible_printers":["{{MachineName}}"],"marker":"{{Marker("FILAMENT", catalog)}}"}""",
                    vendor, "GFSA00", isSelectable: true, compatibleMachineNames: [MachineName], materialType: "PLA", includedTemplateNames: null,
                    sourceVersion: catalog.Version);
                db.FilamentProfiles.Add(filament);
                filaments[catalog] = filament;
            }
        }

        await db.SaveChangesAsync();
        return new Presets(machines, processes, filaments);
    }

    // A printer whose machine, process and filament assignment are the Orca 2.4.2 rows — the catalog the farm default
    // picks from — as an operator would have selected them in the pickers.
    private Task<Printer> SelectedPrinterAsync(Presets presets) =>
        PrinterAsync(machineProfile: presets.Machine[Orca242], processProfile: presets.Process[Orca242], existingFilamentProfile: presets.Filament[Orca242]);

    private async Task<Part> SlicingPartAsync(SlicerRef? pin)
    {
        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IPartFileStorage>();
        var name = Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes(
            $"solid t{name}\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid t{name}\n");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var stream = new MemoryStream(bytes);
        var path = await storage.SaveAsync(hash, "part.stl", stream, default);
        var part = Part.Create($"Part {name}", PartFileFormat.Stl, true, "part.stl", path, bytes.Length, 0.4m,
            pin?.Engine, pin?.Version, EstimateStatus.Available, 60, 20, 1, 1, hash,
            [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
        await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, default);
        return part;
    }

    private async Task StartWorkerAsync(TimeSpan duration)
    {
        using var worker = Worker();
        await worker.StartAsync(default);
        try { await Task.Delay(duration); }
        finally { await worker.StopAsync(default); }
    }

    private async Task<PrintJob> JobAsync(Guid runId) => (await JobsAsync(runId)).Single();

    private static async Task<QueueJobRowResponse> QueueRowAsync(HttpClient client, Guid jobId)
    {
        var response = await client.GetAsync("/api/v1/print-jobs?view=queue&pageSize=1000");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ListQueueJobsResponse>())!.Items.Single(i => i.JobId == jobId);
    }

    private static async Task<PrintJobResponse> JobDetailAsync(HttpClient client, Guid jobId) =>
        (await client.GetFromJsonAsync<PrintJobResponse>($"/api/v1/print-jobs/{jobId}"))!;

    private void AssertNothingSent(Printer printer, PrintJob job)
    {
        Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Should().BeEmpty(
            $"nothing reaches the printer (job state: {job.Status}, printer {job.PrinterId}, dispatched {job.DispatchedAtUtc}, " +
            $"block '{job.LastBlockReason}' x{job.BlockFailureCount}, slicer calls {Spy.CallsFor(printer.Id).Count})");
        job.Status.Should().Be(PrintJobStatus.Queued);
        job.PrinterId.Should().BeNull();
        job.DispatchedAtUtc.Should().BeNull();
    }

    // ---------------------------------------------------------------- the happy paths: each pin gets its own copy

    [Fact]
    public async Task S2_BambuPin_SlicesWithBambuStudiosOwnTwinsOnly_AndRecordsProvenance()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Bambu271);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        PrintJob job;
        try { job = await WaitForAssignmentAsync(run.Id); }
        finally { await worker.StopAsync(default); }

        var call = Spy.CallsFor(printer.Id).Should().ContainSingle().Subject;
        call.Slicer.Should().Be(Bambu271);
        call.Profiles.Pin.Should().Be(Bambu271);
        call.Profiles.MachineSettingsJson.Should().Contain(Marker("MACHINE", Bambu271)).And.NotContain("OrcaSlicer");
        call.Profiles.ProcessSettingsJson.Should().Contain(Marker("PROCESS", Bambu271)).And.NotContain("OrcaSlicer");
        call.Profiles.FilamentSettingsJson.Should().Contain(Marker("FILAMENT", Bambu271)).And.NotContain("OrcaSlicer");
        Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Should().ContainSingle();
        job.PrinterId.Should().Be(printer.Id);

        var detail = await JobDetailAsync(client, job.Id);
        detail.LastBlockReason.Should().BeNull();
        detail.Provenance.Should().NotBeNull();
        detail.Provenance!.Engine.Should().Be("BambuStudio");
        detail.Provenance.Version.Should().Be("2.7.1");
        detail.Provenance.Presets.Should().HaveCount(3).And.OnlyContain(p => p.Outcome == "MappedEngine" && p.SourceVersion == "2.7.1" && p.SettingId != null);
    }

    [Fact]
    public async Task S3_OrcaPin_SlicesWithOrcasOwnPresetsExactly_AsBefore()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Orca242);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try { await WaitForAssignmentAsync(run.Id); }
        finally { await worker.StopAsync(default); }

        var call = Spy.CallsFor(printer.Id).Should().ContainSingle().Subject;
        call.Slicer.Should().Be(Orca242);
        call.Profiles.ProcessSettingsJson.Should().Contain(Marker("PROCESS", Orca242)).And.NotContain("BambuStudio");
        call.Profiles.Process.Outcome.Should().Be(ProfileResolutionOutcome.Exact);
        Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Should().ContainSingle();
    }

    [Fact]
    public async Task S4_ChangingTheFarmDefaultAfterQueueing_NeverMovesTheJobToAnotherSlicer()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Orca242);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        var flip = await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "BambuStudio", version = "2.7.1" });
        try
        {
            flip.StatusCode.Should().Be(HttpStatusCode.OK, await flip.Content.ReadAsStringAsync());
            using var worker = Worker();
            await worker.StartAsync(default);
            try { await WaitForAssignmentAsync(run.Id); }
            finally { await worker.StopAsync(default); }

            Spy.CallsFor(printer.Id).Should().ContainSingle().Which.Slicer.Should().Be(Orca242);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/v1/settings/slicing-defaults", new { engine = "OrcaSlicer", version = "2.4.2" });
        }
    }

    [Fact]
    public async Task S12_FollowLatestJob_SlicesWithThePartsOwnPinNotTheFarmDefault()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Bambu271);
        var client = await ClientAsync();
        // Unlimited lines always follow the part's latest revision; the job is created when the printer is ready for it.
        var run = await RunAsync(client, part.Id, quantity: null, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try { await WaitForAssignmentAsync(run.Id); }
        finally { await worker.StopAsync(default); }

        Spy.CallsFor(printer.Id).Should().ContainSingle().Which.Slicer.Should().Be(Bambu271);
    }

    // ---------------------------------------------------------------- fail closed: nothing is sent, the operator is told

    [Fact]
    public async Task S1_PinnedSlicerHasNoTwinOfTheSelectedProcess_NothingIsSent_AndTheOperatorIsToldWhereToFixIt()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        // Orca has "0.20mm Standard"; Bambu Studio's catalog is loaded (machine and filament twins exist) but lacks it.
        var presets = await SeedAsync(vendor, [Orca242, Bambu271], withoutProcess: [Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Bambu271);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        await StartWorkerAsync(TimeSpan.FromSeconds(2));

        var job = await JobAsync(run.Id);
        AssertNothingSent(printer, job);
        Spy.CallsFor(printer.Id).Should().BeEmpty("no file is made from presets the pinned slicer does not own");
        var row = await QueueRowAsync(client, job.Id);
        row.IsBlocked.Should().BeTrue();
        row.BlockingReason.Should().Contain($"Process profile '{ProcessName}' is not in Bambu Studio 2.7.1")
            .And.Contain($"/printers/{printer.Id}/edit");
    }

    [Fact]
    public async Task S1b_PinnedSlicerHasNoTwinOfTheFilament_NothingIsSent_AndTheFilamentIsNamedWithItsLink()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271], withoutFilament: [Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Bambu271);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        await StartWorkerAsync(TimeSpan.FromSeconds(2));

        var job = await JobAsync(run.Id);
        AssertNothingSent(printer, job);
        Spy.CallsFor(printer.Id).Should().BeEmpty();
        var row = await QueueRowAsync(client, job.Id);
        row.BlockingReason.Should().Contain($"Filament profile '{FilamentName}' is not in Bambu Studio 2.7.1").And.Contain("/filament/");
    }

    [Fact]
    public async Task S5_PinnedVersionNoLongerBundled_NothingIsSent_NeverFallsBackToAnotherVersion()
    {
        var client = await ClientAsync();
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca231, Orca242]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Orca231);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        Strict.Bundle.Remove(Orca231);
        try
        {
            await StartWorkerAsync(TimeSpan.FromSeconds(2));

            AssertNothingSent(printer, await JobAsync(run.Id));
            Spy.CallsFor(printer.Id).Should().BeEmpty("2.4.2 is bundled and has every preset, yet the part is pinned to 2.3.1");
            (await QueueRowAsync(client, (await JobAsync(run.Id)).Id)).BlockingReason.Should().Contain("not bundled");
        }
        finally
        {
            Strict.Bundle.Add(Orca231);
        }
    }

    [Fact]
    public async Task S6_PinnedCatalogNeverLoaded_NothingIsSent_AndTheMessageSaysWhatToDo()
    {
        var client = await ClientAsync();
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var neverIngested = new SlicerRef(SlicerEngine.BambuStudio, "9.9.9");
        Strict.Bundle.Add(neverIngested);
        var presets = await SeedAsync(vendor, [Orca242]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(neverIngested);
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        await StartWorkerAsync(TimeSpan.FromSeconds(2));

        var job = await JobAsync(run.Id);
        AssertNothingSent(printer, job);
        Spy.CallsFor(printer.Id).Should().BeEmpty();
        (await QueueRowAsync(client, job.Id)).BlockingReason.Should().Contain("Bambu Studio 9.9.9 profiles are not loaded").And.Contain("ingest-system-profiles");
    }

    [Fact]
    public async Task S7_SlicingPartWithoutAPin_CannotEvenBeQueued()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(pin: null);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/production-runs", new
        {
            lines = new[] { new { targetKind = "Part", targetId = part.Id, quantity = 1, assignmentMode = "Pinned", pinnedPrinterId = (Guid?)printer.Id } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("NoPin");
        problem.GetProperty("detail").GetString().Should().Contain("no slicer pinned");
    }

    [Fact]
    public async Task S8_PrinterSettingsChangeWhileTheFileIsBeingMade_AbortsTheSend()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271]);
        // A second Orca process the printer will be switched to mid-slice.
        Guid otherProcessId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var other = ProcessProfile.CreateSystemProfile("0.16mm Optimal @BBL X1C", ProfileSourceFormat.OrcaSlicer, null,
                """{"type":"process","layer_height":"0.16"}""", vendor, "GP005", isSelectable: true, compatibleMachineNames: [MachineName], sourceVersion: "2.4.2");
            db.ProcessProfiles.Add(other);
            await db.SaveChangesAsync();
            otherProcessId = other.Id;
        }

        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Orca242);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        Spy.WhileSlicing(printer.Id, async _ =>
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Printers.SingleAsync(p => p.Id == printer.Id)).LinkProcessProfile(otherProcessId);
            await db.SaveChangesAsync();
        });
        try
        {
            await StartWorkerAsync(TimeSpan.FromSeconds(2));

            var job = await JobAsync(run.Id);
            Guid? processNow;
            using (var scope = Factory.Services.CreateScope())
                processNow = (await scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>().Printers.AsNoTracking().SingleAsync(p => p.Id == printer.Id)).ProcessProfileId;
            Spy.CallsFor(printer.Id).Should().NotBeEmpty("the file was made — for settings that no longer apply");
            processNow.Should().Be(otherProcessId, "the settings really did change mid-slice");
            AssertNothingSent(printer, job);
            job.LastBlockReason.Should().Contain("settings changed");
        }
        finally
        {
            Spy.Heal(printer.Id);
        }
    }

    [Fact]
    public async Task S10_SlicingFails_NothingIsSent_TheReasonIsKept_AndRetriesBackOff()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242]);
        var printer = await SelectedPrinterAsync(presets);
        var part = await SlicingPartAsync(Orca242);
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);
        Spy.FailSlicing(printer.Id, new SlicingFailedException("The slicer crashed."));
        try
        {
            await StartWorkerAsync(TimeSpan.FromSeconds(2));

            var job = await JobAsync(run.Id);
            AssertNothingSent(printer, job);
            job.LastBlockReason.Should().Contain("The slicer crashed.");
            job.BlockFailureCount.Should().Be(1);
            Spy.CallsFor(printer.Id).Should().HaveCount(1);
            var detail = await JobDetailAsync(client, job.Id);
            detail.LastBlockReason.Should().Contain("The slicer crashed.");
            (await QueueRowAsync(client, job.Id)).LastBlockReason.Should().Contain("The slicer crashed.", "the queue shows why the last attempt failed");

            // Retrying right away would hit the same failure on every telemetry tick: the job waits out its backoff.
            await StartWorkerAsync(TimeSpan.FromSeconds(2));
            Spy.CallsFor(printer.Id).Should().HaveCount(1, "the job is still in its one-minute backoff");

            // Once the pause has passed it is tried again — and fixed, it goes through and the failure is forgotten.
            await BackdateBlockAsync(job.Id, TimeSpan.FromMinutes(2));
            Spy.Heal(printer.Id);
            using var worker = Worker();
            await worker.StartAsync(default);
            PrintJob sent;
            try { sent = await WaitForAssignmentAsync(run.Id); }
            finally { await worker.StopAsync(default); }

            Spy.CallsFor(printer.Id).Should().HaveCount(2);
            Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Should().ContainSingle();
            sent.LastBlockReason.Should().BeNull();
            sent.BlockFailureCount.Should().Be(0);
        }
        finally
        {
            Spy.Heal(printer.Id);
        }
    }

    private async Task BackdateBlockAsync(Guid jobId, TimeSpan by)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var job = await db.PrintJobs.SingleAsync(j => j.Id == jobId);
        job.RecordBlock(job.LastBlockReason!, job.LastBlockedAtUtc!.Value - by);
        // RecordBlock counts a failure; undo that one so the schedule stays the test's one-failure schedule.
        typeof(PrintJob).GetProperty(nameof(PrintJob.BlockFailureCount))!.SetValue(job, 1);
        await db.SaveChangesAsync();
    }

    // One job whose data cannot be read is blocked and reported — it never takes the queue page down or stops the jobs
    // queued behind it from being sent.
    [Fact]
    public async Task S9_AJobWithUnreadableData_IsBlockedAndReported_WhileTheJobBehindItIsStillSent()
    {
        var client = await ClientAsync();
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242]);
        var printer = await SelectedPrinterAsync(presets);
        var poisonRun = await RunAsync(client, (await SlicingPartAsync(Orca242)).Id, pinned: printer.Id);
        var healthyRun = await RunAsync(client, (await SlicingPartAsync(Orca242)).Id, pinned: printer.Id);
        var poison = await JobAsync(poisonRun.Id);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PrintJobs\" SET \"ProductionSnapshotJson\" = '[]'::jsonb WHERE \"Id\" = {poison.Id}");
        }

        using var worker = Worker();
        await worker.StartAsync(default);
        PrintJob healthy;
        try { healthy = await WaitForAssignmentAsync(healthyRun.Id); }
        finally { await worker.StopAsync(default); }

        healthy.PrinterId.Should().Be(printer.Id);
        Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Should().ContainSingle();
        var stuck = await JobAsync(poisonRun.Id);
        stuck.Status.Should().Be(PrintJobStatus.Queued);
        stuck.PrinterId.Should().BeNull();
        var row = await QueueRowAsync(client, poison.Id);
        row.IsBlocked.Should().BeTrue();
        row.BlockingReason.Should().NotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------- the warning, before anything is queued

    [Fact]
    public async Task Readiness_WarnsAboutAMissingTwin_ForThePinnedSlicerOnly_BeforeAnythingIsQueued()
    {
        var client = await ClientAsync();
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var presets = await SeedAsync(vendor, [Orca242, Bambu271], withoutProcess: [Bambu271]);
        var printer = await SelectedPrinterAsync(presets);

        var bambu = (await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.Slicing.SlicingReadinessResponse>("/api/v1/slicing/readiness?engine=BambuStudio&version=2.7.1"))!;
        var orca = (await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.Slicing.SlicingReadinessResponse>("/api/v1/slicing/readiness?engine=OrcaSlicer&version=2.4.2"))!;

        bambu.Engine.Should().Be("BambuStudio");
        bambu.IsReady.Should().BeFalse();
        var warning = bambu.Printers.Single(p => p.PrinterId == printer.Id).Issues.Should().ContainSingle().Subject;
        warning.Kind.Should().Be("Process");
        warning.Code.Should().Be("Missing");
        warning.Message.Should().Contain($"Process profile '{ProcessName}' is not in Bambu Studio 2.7.1");
        warning.Link.Should().Be($"/printers/{printer.Id}/edit");
        orca.Printers.Single(p => p.PrinterId == printer.Id).Issues.Should().BeEmpty("the farm's own selection is Orca's, so Orca has everything");
    }

    [Fact]
    public async Task Readiness_AskingAboutASlicerThatIsNotBundled_IsRefused()
    {
        var client = await ClientAsync();

        var missing = await client.GetAsync("/api/v1/slicing/readiness?engine=OrcaSlicer&version=0.0.1");
        var nonsense = await client.GetAsync("/api/v1/slicing/readiness?engine=Cura&version=1.0.0");

        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        nonsense.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- pre-sliced files never meet the profile rules

    [Fact]
    public async Task S11_PreSlicedPart_IsSentAsStored_AndNeverTouchesTheSlicerOrThePresets()
    {
        var part = await PartAsync();
        var printer = await PrinterAsync();
        var client = await ClientAsync();
        var run = await RunAsync(client, part.Id, pinned: printer.Id);

        using var worker = Worker();
        await worker.StartAsync(default);
        try { await WaitForAssignmentAsync(run.Id); }
        finally { await worker.StopAsync(default); }

        Spy.CallsFor(printer.Id).Should().BeEmpty();
        Recorder.Started.Where(s => s.SerialNumber == printer.SerialNumber).Should().ContainSingle();
    }
}
