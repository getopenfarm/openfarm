using FluentAssertions;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

public sealed class RepresentativeFilamentResolverTests
{
    private sealed class Fixture
    {
        public FakeFilamentProfileRepository Profiles { get; } = new();
        public FakeMachineProfileRepository Machines { get; } = new();
        public FakeFilamentRepository Filaments { get; } = new();
        public FakeSpoolRepository Spools { get; } = new();
        public FakeFeedUnitRepository Units { get; } = new();
        public Printer Printer { get; } = Printer.Create("P1", "Bambu", "P1S", "127.0.0.1", "SN");
        public Filament Filament { get; }
        public FilamentVariant Black { get; }
        public FilamentProfile Default { get; }
        public RepresentativeFilamentResolver Resolver { get; }
        public FilamentProfileAssignmentService Assignments { get; }

        public Fixture()
        {
            var machine = MachineProfile.Create("P1S 0.4", ProfileSourceFormat.OrcaSlicer, null,
                """{"printer_model":"Bambu Lab P1S","nozzle_diameter":["0.4"]}""");
            Machines.AddAsync(machine, default).GetAwaiter().GetResult();
            Printer.LinkMachineProfile(machine.Id);
            Printer.ObserveNozzle(0.4m, DateTimeOffset.UtcNow);
            var printers = new FakePrinterRepository(Printer);
            Default = FilamentProfile.Create("My PLA", ProfileSourceFormat.OrcaSlicer, null,
                """{"filament_type":["PLA"],"compatible_printers":["P1S 0.4"]}""");
            Profiles.AddAsync(Default, default).GetAwaiter().GetResult();
            Filament = Filament.Create(Guid.NewGuid(), "PLA Basic", "PLA");
            Black = Filament.AddVariant("Black", "#000000");
            Filaments.AddAsync(Filament, default).GetAwaiter().GetResult();
            var compatibility = new ProfileCompatibility(Machines, Profiles);
            Resolver = new(Profiles, Spools, Filaments, Units, printers, Machines, compatibility);
            Assignments = new(Profiles, printers, compatibility, new FakeSlicingDefaultsQuery(), new ProfileResolver(Machines, new FakeProcessProfileRepository(), Profiles));
        }

        public async Task LoadAsync()
        {
            var unit = FeedUnit.CreateConnectorDerived(Printer.Id, FeedUnitKindCatalog.MultiMaterialUnit, 0);
            var slot = unit.AddFeedSlot(0);
            await Units.AddAsync(unit, default);
            var spool = Spool.Create(Black.Id, null, 1000, 1000);
            spool.LoadOntoFeedSlot(slot.Id);
            await Spools.AddAsync(spool, default);
        }

        public Task AssignAsync(string? color = null, Guid? profile = null) => Assignments.ReplaceAsync(Filament,
            [new("Bambu", "P1S", 0.4m, profile ?? Default.Id, color)], default);
    }

    [Fact]
    public async Task SelectedVariantOverridesFileColourButStillRequiresThatExactVariant()
    {
        var f = new Fixture(); await f.LoadAsync(); await f.AssignAsync();
        var result = await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", "#ffffff", f.Black.Id, null, default);
        result.FilamentVariantId.Should().Be(f.Black.Id);
        await ((Func<Task>)(() => f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", "#000000", Guid.NewGuid(), null, default)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task StandbyReloaderSpoolCannotSatisfyReadiness()
    {
        var f = new Fixture(); await f.AssignAsync();
        var unit = FeedUnit.CreateAutomaticFilamentReloader(f.Printer.Id, 0, "Reloader", null);
        var standby = unit.AddFeedSlot(0);
        var feeding = unit.AddFeedSlot(1);
        unit.SelectFeedingSlot(feeding.Id);
        await f.Units.AddAsync(unit, default);
        var spool = Spool.Create(f.Black.Id, null, 1000, 1000);
        spool.LoadOntoFeedSlot(standby.Id);
        await f.Spools.AddAsync(spool, default);
        await ((Func<Task>)(() => f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, f.Black.Id, null, default)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*tracked spool*");
        unit.SelectFeedingSlot(standby.Id);
        (await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, f.Black.Id, null, default)).SpoolId.Should().Be(spool.Id);
        unit.Archive();
        await ((Func<Task>)(() => f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, f.Black.Id, null, default)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*tracked spool*");
    }

    [Fact]
    public async Task LoadedFilamentUsesHardwareAssignment()
    {
        var f = new Fixture(); await f.LoadAsync(); await f.AssignAsync();
        var result = await f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default);
        result.Profile.Id.Should().Be(f.Default.Id);
    }

    [Fact]
    public async Task MissingAssignmentDoesNotFallBackToCatalog()
    {
        var f = new Fixture(); await f.LoadAsync();
        var act = () => f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*Assign a profile*");
    }

    [Fact]
    public async Task NoTrackedSpoolDoesNotUseStorageOrCatalog()
    {
        var f = new Fixture(); await f.AssignAsync();
        var act = () => f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*tracked spool*");
    }

    [Fact]
    public async Task ReconnectionNeedsFreshNozzleTelemetry()
    {
        var f = new Fixture(); await f.LoadAsync(); await f.AssignAsync(); f.Printer.BeginConnection();
        var act = () => f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*current connection*");
    }

    [Fact]
    public async Task ColourOverrideWinsAndClearingRestoresDefault()
    {
        var f = new Fixture(); await f.LoadAsync();
        var tuned = FilamentProfile.Create("Black PLA", ProfileSourceFormat.OrcaSlicer, null, """{"filament_type":["PLA"]}""");
        await f.Profiles.AddAsync(tuned, default);
        await f.Assignments.ReplaceAsync(f.Filament, [new("Bambu", "P1S", 0.4m, f.Default.Id), new("Bambu", "P1S", 0.4m, tuned.Id, "#000000")], default);
        (await f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default)).Profile.Id.Should().Be(tuned.Id);
        await f.AssignAsync();
        (await f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default)).Profile.Id.Should().Be(f.Default.Id);
    }

    [Fact]
    public async Task InvalidAssignmentDoesNotPartiallyReplaceExistingAssignments()
    {
        var f = new Fixture(); await f.AssignAsync();
        var act = () => f.Assignments.ReplaceAsync(f.Filament, [new("Bambu", "P1S", 0.6m, f.Default.Id)], default);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        f.Filament.ProfileAssignments.Should().ContainSingle().Which.NozzleDiameterMm.Should().Be(0.4m);
    }

    [Fact]
    public async Task NozzleChangeRejectsOldMachinePreset()
    {
        var f = new Fixture(); await f.LoadAsync(); await f.AssignAsync(); f.Printer.ObserveNozzle(0.6m, DateTimeOffset.UtcNow);
        var act = () => f.Resolver.ResolveAsync(f.Printer.Id, "PLA", default);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*does not match*");
    }

    [Fact]
    public async Task DuplicateAndForeignColourAssignmentsAreRejected()
    {
        var f = new Fixture();
        var act = () => f.Assignments.ReplaceAsync(f.Filament, [new("Bambu", "P1S", 0.4m, f.Default.Id), new("Bambu", "P1S", 0.4m, f.Default.Id)], default);
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*Duplicate*");
        act = () => f.AssignAsync("#ffffff");
        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*colour override*");
    }
    [Fact]
    public async Task UnusedLoadedSpoolWithoutAssignmentDoesNotBlockAnAssignedMatchingSpool()
    {
        var f = new Fixture(); await f.LoadAsync(); await f.AssignAsync();
        var assignedSpool = (await f.Spools.ListAsync(null, null, default))[0];
        var unused = Filament.Create(Guid.NewGuid(), "Other PLA", "PLA");
        var variant = unused.AddVariant("Black", "#000000");
        await f.Filaments.AddAsync(unused, default);
        // The second spool goes in another slot of the same AMS: with an AMS attached the external spool is not a usable feed.
        var unit = (await f.Units.GetByPrinterIdAsync(f.Printer.Id, default)).Single(u => u.Kind == FeedUnitKindCatalog.MultiMaterialUnit);
        var slot = unit.AddFeedSlot(unit.FeedSlots.Count);
        var unassignedSpool = Spool.Create(variant.Id, null, 1000, 1000);
        var expectedSpoolId = assignedSpool.Id;
        var missingVariantId = variant.Id;
        if (unassignedSpool.Id.CompareTo(assignedSpool.Id) > 0)
        {
            f.Filament.ReplaceProfileAssignments([]);
            await f.Assignments.ReplaceAsync(unused, [new("Bambu", "P1S", 0.4m, f.Default.Id)], default);
            expectedSpoolId = unassignedSpool.Id;
            missingVariantId = f.Black.Id;
        }
        unassignedSpool.LoadOntoFeedSlot(slot.Id);
        await f.Spools.AddAsync(unassignedSpool, default);
        (await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", "#000000", null, null, default)).SpoolId.Should().Be(expectedSpoolId);
        await ((Func<Task>)(() => f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", "#000000", missingVariantId, null, default)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*Assign a profile*");
    }


    private static async Task<(FeedUnit Unit, List<Spool> Spools)> LoadAmsAsync(Fixture f, params decimal[] remainingGrams)
    {
        var unit = FeedUnit.CreateConnectorDerived(f.Printer.Id, FeedUnitKindCatalog.MultiMaterialUnit, 0);
        var spools = new List<Spool>();
        for (var slot = 0; slot < remainingGrams.Length; slot++)
        {
            var spool = Spool.Create(f.Black.Id, null, 1000, remainingGrams[slot]);
            spool.LoadOntoFeedSlot(unit.AddFeedSlot(slot).Id);
            await f.Spools.AddAsync(spool, default);
            spools.Add(spool);
        }

        await f.Units.AddAsync(unit, default);
        return (unit, spools);
    }

    [Fact]
    public async Task WithAnEstimate_TakesTheEmptiestSpoolThatHoldsTheJob()
    {
        var f = new Fixture(); await f.AssignAsync();
        var (unit, spools) = await LoadAmsAsync(f, 1000, 200, 100);

        var chosen = await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, f.Black.Id, null, default, estimatedGrams: 150);

        // 200 g holds 150 g + 10% on its own; the 100 g spool would need the AMS to continue onto another.
        chosen.SpoolId.Should().Be(spools[1].Id);
        chosen.AvailableGrams.Should().Be(1300);
        chosen.FeedUnitId.Should().Be(unit.Id);
        chosen.Label.Should().Be("Black PLA");
    }

    [Fact]
    public async Task WithoutAnEstimate_KeepsTheLowestSlot()
    {
        var f = new Fixture(); await f.AssignAsync();
        var (_, spools) = await LoadAmsAsync(f, 1000, 200, 100);

        var chosen = await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, f.Black.Id, null, default);

        chosen.SpoolId.Should().Be(spools[0].Id);
    }

    private static FilamentProfile SystemPla(string version, string externalId = "GFSA00") => FilamentProfile.CreateSystemProfile(
        "Bambu PLA Basic @BBL X1C", ProfileSourceFormat.OrcaSlicer, null, """{"filament_type":["PLA"]}""",
        "Bambu Lab", externalId, isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null,
        sourceVersion: version);

    [Fact]
    public async Task NarrowedProfileOfAnotherSlicerVersion_IsTheSamePresetNotAMismatch()
    {
        var f = new Fixture(); await f.LoadAsync();
        var narrowedInOldVersion = SystemPla("2.3.1");
        var assignedInCurrentVersion = SystemPla("2.4.2");
        await f.Profiles.AddAsync(narrowedInOldVersion, default);
        await f.Profiles.AddAsync(assignedInCurrentVersion, default);
        await f.AssignAsync(profile: assignedInCurrentVersion.Id);

        var result = await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, null, narrowedInOldVersion.Id, default);

        result.Profile.Id.Should().Be(assignedInCurrentVersion.Id);
    }

    [Fact]
    public async Task NarrowedProfileThatIsADifferentPreset_IsStillAMismatch()
    {
        var f = new Fixture(); await f.LoadAsync();
        var narrowed = SystemPla("2.3.1", externalId: "GFSA01");
        var assigned = SystemPla("2.4.2");
        await f.Profiles.AddAsync(narrowed, default);
        await f.Profiles.AddAsync(assigned, default);
        await f.AssignAsync(profile: assigned.Id);

        var act = async () => await f.Resolver.ResolveLoadedAsync(f.Printer.Id, "PLA", null, null, narrowed.Id, default);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*does not match the part's required filament profile*");
    }
}
