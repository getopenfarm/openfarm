using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Production;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Production;

// Dispatch makes the file, then compares this stamp with a fresh one right before sending: equal stamps mean the same
// file would be made again. So the stamp must move with everything the file depends on — in particular which presets
// of which slicer version were resolved, not only which rows the operator selected.
public class ProductionConfigurationStampTests
{
    private readonly Printer _printer = Printer.Create("Printer A", "System", "Simulation", "10.0.0.5", "SN-A");
    private readonly TwoEngineCatalog _catalog = new();
    private readonly FilamentProfile _filament;

    public ProductionConfigurationStampTests()
    {
        _filament = _catalog.Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], Bambu271)[Bambu271];
        _printer.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
    }

    private static readonly Guid MachineId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ProcessId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid FilamentId = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    private static ResolvedPreset Preset(ProfileKind kind, string name, Guid resolvedId, string settingId) =>
        new(kind, Guid.Parse("00000000-0000-0000-0000-0000000000f0"), name, resolvedId, name, ProfileSourceFormat.BambuStudio, "2.7.1",
            settingId, BambuVendor, ProfileResolutionOutcome.MappedEngine);

    private static ResolvedSlicingProfiles Baseline() => new(Bambu271, """{"machine":1}""", """{"process":1}""", """{"filament":1}""",
        Preset(ProfileKind.Machine, MachineName, MachineId, "GM014"),
        Preset(ProfileKind.Process, "0.20mm Standard @BBL X1C", ProcessId, "GP004"),
        Preset(ProfileKind.Filament, "Bambu PLA Basic @BBL X1C", FilamentId, "GFSA00"));

    private async Task<string> StampAsync(ResolvedSlicingProfiles? profiles)
    {
        var readiness = new ProductionProfileReadiness(null!, new FakePrinterRepository(_printer), null!,
            new ProfileCompatibility(_catalog.Machines, _catalog.Filaments), null!, null!, null!, null!, null!, NullLogger<ProductionProfileReadiness>.Instance);
        var selections = new[] { new ProductionFilamentSelection(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), Guid.Parse("00000000-0000-0000-0000-0000000000c2"),
            Guid.Parse("00000000-0000-0000-0000-0000000000c3"), "PLA", _filament, 0.4m) };

        return await readiness.StampAsync(_printer.Id, new ProductionResolution(selections, profiles), default);
    }

    [Fact]
    public async Task SameConfiguration_GivesTheSameStamp()
    {
        var first = await StampAsync(Baseline());
        var second = await StampAsync(Baseline());

        second.Should().Be(first);
    }

    [Fact]
    public async Task AnythingTheFileIsMadeFrom_MovesTheStamp()
    {
        var baseline = await StampAsync(Baseline());
        var changes = new Dictionary<string, ResolvedSlicingProfiles?>
        {
            ["pinned slicer version"] = Baseline() with { Pin = new SlicerRef(Bambu271.Engine, "2.7.2") },
            ["pinned slicer engine"] = Baseline() with { Pin = Orca242 },
            ["machine row it resolved to"] = Baseline() with { Machine = Baseline().Machine with { ResolvedId = Guid.NewGuid() } },
            ["process row it resolved to"] = Baseline() with { Process = Baseline().Process with { ResolvedId = Guid.NewGuid() } },
            ["filament row it resolved to"] = Baseline() with { Filament = Baseline().Filament with { ResolvedId = Guid.NewGuid() } },
            ["filament setting_id"] = Baseline() with { Filament = Baseline().Filament with { SettingId = "GFSA00_34" } },
            ["catalog version of a preset"] = Baseline() with { Process = Baseline().Process with { SourceVersion = "2.7.0" } },
            ["how a preset was reached"] = Baseline() with { Process = Baseline().Process with { Outcome = ProfileResolutionOutcome.Exact } },
            ["machine settings"] = Baseline() with { MachineSettingsJson = """{"machine":2}""" },
            ["process settings"] = Baseline() with { ProcessSettingsJson = """{"process":2}""" },
            ["filament settings"] = Baseline() with { FilamentSettingsJson = """{"filament":2}""" },
            ["nothing to slice"] = null,
        };

        foreach (var (what, changed) in changes)
        {
            (await StampAsync(changed)).Should().NotBe(baseline, $"the stamp must change with the {what}");
        }
    }

    [Fact]
    public async Task PrinterConfiguration_MovesTheStamp()
    {
        var before = await StampAsync(Baseline());

        _printer.LinkProcessProfile(Guid.NewGuid());

        (await StampAsync(Baseline())).Should().NotBe(before);
    }
}
