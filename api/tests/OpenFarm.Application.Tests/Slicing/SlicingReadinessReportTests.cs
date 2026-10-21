using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Slicing;
using OpenFarm.Application.Tests.Parts.Fakes;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Slicing;

// The warning an operator sees before queueing anything: for a slicer pin, which printers and filament assignments
// cannot be sliced with — computed by the very checks production enforces, so the two can never disagree.
public class SlicingReadinessReportTests
{
    private sealed class Farm
    {
        public TwoEngineCatalog Catalog { get; } = new();
        public Printer Printer { get; } = Printer.Create("Printer A", "System", "Simulation", "10.0.0.5", "SN-A");
        public Filament Filament { get; } = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
        public FakeFilamentRepository Filaments { get; } = new();
        public FakePrinterRepository Printers { get; }

        public Farm()
        {
            Printers = new FakePrinterRepository(Printer);
        }

        // The operator's selection lives in the farm default's (Orca 2.4.2) catalog.
        public void Select(MachineProfile machine, ProcessProfile process, FilamentProfile filament)
        {
            Printer.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
            Printer.LinkMachineProfile(machine.Id);
            Printer.LinkProcessProfile(process.Id);
            Printer.SelectBuildPlate(BuildPlateType.HighTemp);
            Filament.AddVariant("Black", "#000000");
            Filament.ReplaceProfileAssignments([FilamentProfileAssignment.Create(Filament.Id, "System", "Simulation", 0.4m, filament.Id)]);
            Filaments.AddAsync(Filament, default).GetAwaiter().GetResult();
        }

        // The same selection, plate and hardware as the first printer: a fleet of one model.
        public async Task AddIdenticalPrintersAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var copy = Printer.Create($"Printer {i + 2}", "System", "Simulation", $"10.0.0.{i + 10}", $"SN-{i + 2}");
                copy.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
                copy.LinkMachineProfile(Printer.MachineProfileId!.Value);
                copy.LinkProcessProfile(Printer.ProcessProfileId!.Value);
                copy.SelectBuildPlate(BuildPlateType.HighTemp);
                await Printers.AddAsync(copy, default);
            }
        }

        public GetSlicingReadinessQueryHandler Handler(params SlicerRef[] bundled)
        {
            var compatibility = new ProfileCompatibility(Catalog.Machines, Catalog.Filaments);
            var selection = new ResolveSlicingProfileSelection(Printers, Catalog.Machines, Catalog.Processes, Catalog.Filaments,
                new RepresentativeFilamentResolver(Catalog.Filaments, new FakeSpoolRepository(), Filaments, new FakeFeedUnitRepository(), Printers, Catalog.Machines, compatibility),
                compatibility, Catalog.Resolver);
            return new(
                new ResolveSlicerSelection(new FakeAvailableSlicerCatalog(bundled.Select(b => new AvailableSlicer(b.Engine, b.Version, SlicerPlatform.Linux, $"/bundle/{b.Engine}/{b.Version}")).ToArray()),
                    new FakeHostPlatformProvider(SlicerPlatform.Linux)),
                Printers, Filaments, Catalog.Machines, Catalog.Processes, Catalog.Filaments, Catalog.Resolver, compatibility, selection);
        }
    }

    private static Farm FarmWithTwins(params SlicerRef[] catalogs)
    {
        var farm = new Farm();
        var (machine, process, filament) = farm.Catalog.StandardSet(catalogs);
        farm.Select(machine[Orca242], process[Orca242], filament[Orca242]);
        return farm;
    }

    [Theory]
    [InlineData("OrcaSlicer", "2.4.2")]
    [InlineData("BambuStudio", "2.7.1")]
    public async Task EveryPresetResolvesInThePinnedCatalog_TheFarmIsReady(string engine, string version)
    {
        var farm = FarmWithTwins(Orca242, Bambu271);

        var report = await farm.Handler(Orca242, Bambu271).HandleAsync(new(Enum.Parse<SlicerEngine>(engine), version), default);

        report.IsReady.Should().BeTrue();
        report.Printers.Should().ContainSingle().Which.Issues.Should().BeEmpty();
        report.Filaments.Should().ContainSingle().Which.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessWithNoTwinInThePinnedSlicer_IsReportedOnThePrinterWithItsLink()
    {
        var farm = new Farm();
        var machine = farm.Catalog.Machine(BambuVendor, MachineName, "GM014", Orca242, Bambu271);
        var process = farm.Catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], Orca242);
        var filament = farm.Catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca242, Bambu271);
        farm.Select(machine[Orca242], process[Orca242], filament[Orca242]);

        var report = await farm.Handler(Orca242, Bambu271).HandleAsync(new(SlicerEngine.BambuStudio, "2.7.1"), default);

        report.IsReady.Should().BeFalse();
        var issue = report.Printers.Single().Issues.Should().ContainSingle().Subject;
        issue.Kind.Should().Be("Process");
        issue.Code.Should().Be("Missing");
        issue.Message.Should().Contain("Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1");
        issue.Link.Should().Be($"/printers/{farm.Printer.Id}/edit");
        report.Filaments.Single().Issues.Should().BeEmpty("the filament has its twin");
    }

    [Fact]
    public async Task FilamentWithNoTwinInThePinnedSlicer_IsReportedOnTheAssignmentWithItsLink()
    {
        var farm = new Farm();
        var machine = farm.Catalog.Machine(BambuVendor, MachineName, "GM014", Orca242, Bambu271);
        var process = farm.Catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], Orca242, Bambu271);
        var filament = farm.Catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Orca242);
        farm.Select(machine[Orca242], process[Orca242], filament[Orca242]);

        var report = await farm.Handler(Orca242, Bambu271).HandleAsync(new(SlicerEngine.BambuStudio, "2.7.1"), default);

        report.Printers.Single().Issues.Should().BeEmpty();
        var entry = report.Filaments.Should().ContainSingle().Subject;
        entry.ProfileName.Should().Be("Bambu PLA Basic @BBL X1C");
        entry.Issues.Should().ContainSingle().Which.Should().Match<ReadinessIssue>(i =>
            i.Kind == "Filament" && i.Code == "Missing" && i.Link == $"/filament/{farm.Filament.Id}/edit");
    }

    [Fact]
    public async Task PinnedCatalogNeverLoaded_EveryPresetIsReportedAsNotLoaded_NotAsMissing()
    {
        var farm = FarmWithTwins(Orca242);
        var neverIngested = new SlicerRef(SlicerEngine.BambuStudio, "9.9.9");

        var report = await farm.Handler(Orca242, neverIngested).HandleAsync(new(neverIngested.Engine, neverIngested.Version), default);

        report.Printers.Single().Issues.Select(i => i.Code).Should().OnlyContain(code => code == "CatalogNotLoaded");
        report.Filaments.Single().Issues.Select(i => i.Code).Should().OnlyContain(code => code == "CatalogNotLoaded");
        report.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task PrinterWithNothingSelected_IsToldWhatToSelect()
    {
        var farm = new Farm();
        farm.Catalog.StandardSet(Orca242);

        var report = await farm.Handler(Orca242).HandleAsync(new(SlicerEngine.OrcaSlicer, "2.4.2"), default);

        report.Printers.Single().Issues.Select(i => (i.Kind, i.Code)).Should().BeEquivalentTo(new[] { ("Machine", "NotSet"), ("Process", "NotSet"), ("Plate", "NotSet") });
    }

    [Fact]
    public async Task SlicerThatIsNotBundled_IsRefusedNotReported()
    {
        var farm = FarmWithTwins(Orca242);

        var act = () => farm.Handler(Orca242).HandleAsync(new(SlicerEngine.BambuStudio, "2.7.1"), default);

        await act.Should().ThrowAsync<SlicerUnavailableException>();
    }

    // A farm is usually many printers of a few models. They share one answer per filament assignment, so the work of a
    // report must not grow with the fleet.
    [Fact]
    public async Task IdenticalPrinters_ShareOneCheck_SoTheWorkDoesNotGrowWithTheFleet()
    {
        var single = FarmWithTwins(Orca242, Bambu271);
        await single.Handler(Orca242, Bambu271).HandleAsync(new(SlicerEngine.BambuStudio, "2.7.1"), default);
        var readsForOnePrinter = single.Catalog.Filaments.GetByIdCalls;
        readsForOnePrinter.Should().BeGreaterThan(0, "the check really reads the filament profile");

        var fleet = FarmWithTwins(Orca242, Bambu271);
        await fleet.AddIdenticalPrintersAsync(7);
        var report = await fleet.Handler(Orca242, Bambu271).HandleAsync(new(SlicerEngine.BambuStudio, "2.7.1"), default);

        fleet.Catalog.Filaments.GetByIdCalls.Should().Be(readsForOnePrinter);
        report.IsReady.Should().BeTrue();
        report.Printers.Should().HaveCount(8);
        report.Filaments.Should().ContainSingle().Which.PrinterIds.Should().HaveCount(8, "every printer is still listed under the assignment");
    }

    // The report is only useful if it agrees with enforcement: whatever it calls ready must slice, whatever it blocks must not.
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task ReportAgreesWithTheFunnelForEveryCombinationOfTwinsPresent(bool machineTwin, bool processTwin, bool filamentTwin)
    {
        var farm = new Farm();
        var machine = farm.Catalog.Machine(BambuVendor, MachineName, "GM014", machineTwin ? [Orca242, Bambu271] : [Orca242]);
        var process = farm.Catalog.Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], processTwin ? [Orca242, Bambu271] : [Orca242]);
        var filament = farm.Catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], filamentTwin ? [Orca242, Bambu271] : [Orca242]);
        farm.Catalog.Loaded(Bambu271);
        farm.Select(machine[Orca242], process[Orca242], filament[Orca242]);
        var pin = Bambu271;
        var compatibility = new ProfileCompatibility(farm.Catalog.Machines, farm.Catalog.Filaments);
        var selection = new ResolveSlicingProfileSelection(farm.Printers, farm.Catalog.Machines, farm.Catalog.Processes, farm.Catalog.Filaments,
            new RepresentativeFilamentResolver(farm.Catalog.Filaments, new FakeSpoolRepository(), farm.Filaments, new FakeFeedUnitRepository(), farm.Printers, farm.Catalog.Machines, compatibility),
            compatibility, farm.Catalog.Resolver);
        var use = new ProfileUseContext("System", "Simulation", 0.4m, "PLA");

        var report = await farm.Handler(Orca242, Bambu271).HandleAsync(new(pin.Engine, pin.Version), default);
        var enforcement = async () => await selection.ResolveProfilesAsync(machine[Orca242].Id, process[Orca242].Id, filament[Orca242].Id, BuildPlateType.HighTemp, pin, use, default);

        if (machineTwin && processTwin && filamentTwin)
        {
            report.IsReady.Should().BeTrue();
            await enforcement.Should().NotThrowAsync();
        }
        else
        {
            report.IsReady.Should().BeFalse();
            await enforcement.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        }
    }
}
