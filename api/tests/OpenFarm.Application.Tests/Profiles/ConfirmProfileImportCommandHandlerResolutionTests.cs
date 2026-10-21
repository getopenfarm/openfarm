using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

// TDD (write-first) per research.md §3 — the two-pass base/override resolution algorithm is a
// pure, deterministic, well-specified function over already-parsed data, the same bar the
// constitution sets for BOM decomposition and filament weight math. Process profiles are never
// importable after 009 (FR-011) — no Process-typed coverage remains here.
public class ConfirmProfileImportCommandHandlerResolutionTests
{
    private static ConfirmProfileImportCommandHandler CreateHandler(
        FakeProfileBundleParser parser, FakeMachineProfileRepository machineRepo, FakeFilamentProfileRepository filamentRepo) =>
        new(parser, machineRepo, filamentRepo);

    private static ConfirmProfileImportCommand Command(IReadOnlyList<SelectedProfileKey> selections) =>
        new(new MemoryStream(), "bundle.zip", selections);

    [Fact]
    public async Task Resolve_InBatchMatch_ResolvesBaseProfileId()
    {
        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [],
                [
                    new ParsedFilamentPreset("Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}"),
                    new ParsedFilamentPreset("Generic PLA - Red", ProfileSourceFormat.OrcaSlicer, "Generic PLA", "{}"),
                ]),
        };
        var machineRepo = new FakeMachineProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var handler = CreateHandler(parser, machineRepo, filamentRepo);

        var result = await handler.HandleAsync(
            Command([
                new SelectedProfileKey(ProfileType.Filament, "Generic PLA", null),
                new SelectedProfileKey(ProfileType.Filament, "Generic PLA - Red", null),
            ]),
            CancellationToken.None);

        var basePresetId = result.CreatedFilamentProfiles.Single(p => p.Name == "Generic PLA").Id;
        var overridePreset = result.CreatedFilamentProfiles.Single(p => p.Name == "Generic PLA - Red");
        overridePreset.BaseProfileId.Should().Be(basePresetId);
    }

    [Fact]
    public async Task Resolve_PriorImportMatch_ResolvesBaseProfileId()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        var existingBase = FilamentProfile.Create(
            "Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}");
        await filamentRepo.AddAsync(existingBase, CancellationToken.None);

        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [],
                [new ParsedFilamentPreset("Generic PLA - Red", ProfileSourceFormat.OrcaSlicer, "Generic PLA", "{}")]),
        };
        var handler = CreateHandler(parser, new FakeMachineProfileRepository(), filamentRepo);

        var result = await handler.HandleAsync(
            Command([new SelectedProfileKey(ProfileType.Filament, "Generic PLA - Red", null)]),
            CancellationToken.None);

        result.CreatedFilamentProfiles.Single().BaseProfileId.Should().Be(existingBase.Id);
    }

    [Fact]
    public async Task Resolve_NoMatch_LeavesBaseProfileIdNullButKeepsBaseProfileName()
    {
        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [],
                [new ParsedFilamentPreset("Generic PLA - Red", ProfileSourceFormat.OrcaSlicer, "Some Built-In Default", "{}")]),
        };
        var filamentRepo = new FakeFilamentProfileRepository();
        var handler = CreateHandler(parser, new FakeMachineProfileRepository(), filamentRepo);

        var result = await handler.HandleAsync(
            Command([new SelectedProfileKey(ProfileType.Filament, "Generic PLA - Red", null)]),
            CancellationToken.None);

        result.CreatedFilamentProfiles.Single().BaseProfileId.Should().BeNull();
        filamentRepo.Rows.Single().BaseProfileName.Should().Be("Some Built-In Default");
    }

    [Fact]
    public async Task Resolve_AmbiguousInBatchMatch_LeavesBaseProfileIdNull()
    {
        // FR-009 permits importing a duplicate-named preset as a separate row — two same-named
        // presets in the same batch make a base-name lookup against that name ambiguous.
        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [],
                [
                    new ParsedFilamentPreset("Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}"),
                    new ParsedFilamentPreset("Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}"),
                    new ParsedFilamentPreset("Generic PLA - Red", ProfileSourceFormat.OrcaSlicer, "Generic PLA", "{}"),
                ]),
        };
        var handler = CreateHandler(parser, new FakeMachineProfileRepository(), new FakeFilamentProfileRepository());

        var result = await handler.HandleAsync(
            Command([
                new SelectedProfileKey(ProfileType.Filament, "Generic PLA", ProfileDuplicateAction.ImportAsNew),
                new SelectedProfileKey(ProfileType.Filament, "Generic PLA", ProfileDuplicateAction.ImportAsNew),
                new SelectedProfileKey(ProfileType.Filament, "Generic PLA - Red", null),
            ]),
            CancellationToken.None);

        result.CreatedFilamentProfiles.Should().HaveCount(3);
        result.CreatedFilamentProfiles.Single(p => p.Name == "Generic PLA - Red").BaseProfileId.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_AmbiguousPriorImportMatch_LeavesBaseProfileIdNull()
    {
        var filamentRepo = new FakeFilamentProfileRepository();
        await filamentRepo.AddAsync(
            FilamentProfile.Create("Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}"),
            CancellationToken.None);
        await filamentRepo.AddAsync(
            FilamentProfile.Create("Generic PLA", ProfileSourceFormat.OrcaSlicer, null, "{}"),
            CancellationToken.None);

        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [],
                [new ParsedFilamentPreset("Generic PLA - Red", ProfileSourceFormat.OrcaSlicer, "Generic PLA", "{}")]),
        };
        var handler = CreateHandler(parser, new FakeMachineProfileRepository(), filamentRepo);

        var result = await handler.HandleAsync(
            Command([new SelectedProfileKey(ProfileType.Filament, "Generic PLA - Red", null)]),
            CancellationToken.None);

        result.CreatedFilamentProfiles.Single().BaseProfileId.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_CrossFormatSameName_DoesNotResolve()
    {
        // A Bambu Studio preset can never legitimately resolve against an OrcaSlicer-named row
        // (research.md §3) — scoped by (SourceFormat, Name), not Name alone.
        var filamentRepo = new FakeFilamentProfileRepository();
        await filamentRepo.AddAsync(
            FilamentProfile.Create("Generic PLA", ProfileSourceFormat.BambuStudio, null, "{}"),
            CancellationToken.None);

        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [],
                [new ParsedFilamentPreset("Generic PLA - Red", ProfileSourceFormat.OrcaSlicer, "Generic PLA", "{}")]),
        };
        var handler = CreateHandler(parser, new FakeMachineProfileRepository(), filamentRepo);

        var result = await handler.HandleAsync(
            Command([new SelectedProfileKey(ProfileType.Filament, "Generic PLA - Red", null)]),
            CancellationToken.None);

        result.CreatedFilamentProfiles.Single().BaseProfileId.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_MachineProfileBaseOverride_AlsoResolves()
    {
        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [
                    new ParsedMachinePreset("Generic Printer", ProfileSourceFormat.OrcaSlicer, null, "{}"),
                    new ParsedMachinePreset("Generic Printer - Direct Drive", ProfileSourceFormat.OrcaSlicer, "Generic Printer", "{}"),
                ],
                []),
        };
        var machineRepo = new FakeMachineProfileRepository();
        var handler = CreateHandler(parser, machineRepo, new FakeFilamentProfileRepository());

        var result = await handler.HandleAsync(
            Command([
                new SelectedProfileKey(ProfileType.Machine, "Generic Printer", null),
                new SelectedProfileKey(ProfileType.Machine, "Generic Printer - Direct Drive", null),
            ]),
            CancellationToken.None);

        var baseId = result.CreatedMachineProfiles.Single(p => p.Name == "Generic Printer").Id;
        result.CreatedMachineProfiles.Single(p => p.Name == "Generic Printer - Direct Drive").BaseProfileId.Should().Be(baseId);
    }

    // With two ingested versions the same System base name matches one row per version; the catalog version's
    // row must win, or every imported override's base would be left unresolved.
    [Theory]
    [InlineData("2.4.2", "2.4.2")]
    [InlineData("2.3.1", "2.3.1")]
    public async Task Resolve_SystemBaseInSeveralVersions_PrefersTheCatalogVersion(string catalogVersion, string expectedBaseVersion)
    {
        var (handler, rows) = await HandlerWithSystemBasesAsync(catalogVersion, "2.3.1", "2.4.2");

        var result = await handler.HandleAsync(
            Command([new SelectedProfileKey(ProfileType.Filament, "Bambu PLA - Red", null)]), CancellationToken.None);

        result.CreatedFilamentProfiles.Single().BaseProfileId.Should().Be(rows[expectedBaseVersion].Id);
    }

    [Fact]
    public async Task Resolve_SystemBaseOnlyInAnotherVersion_FallsBackToTheNewestVersionThatHasIt()
    {
        var (handler, rows) = await HandlerWithSystemBasesAsync("2.4.2", "2.3.1");

        var result = await handler.HandleAsync(
            Command([new SelectedProfileKey(ProfileType.Filament, "Bambu PLA - Red", null)]), CancellationToken.None);

        result.CreatedFilamentProfiles.Single().BaseProfileId.Should().Be(rows["2.3.1"].Id);
    }

    // The base exists as a System row in each of `baseVersions`; the catalog version is `catalogVersion`
    // (its machine catalog is ingested too, so the presence rule selects it).
    private static async Task<(ConfirmProfileImportCommandHandler Handler, Dictionary<string, FilamentProfile> Rows)> HandlerWithSystemBasesAsync(
        string catalogVersion, params string[] baseVersions)
    {
        var machineRepo = new FakeMachineProfileRepository();
        var filamentRepo = new FakeFilamentProfileRepository();
        var rows = new Dictionary<string, FilamentProfile>();
        foreach (var version in baseVersions)
        {
            rows[version] = FilamentProfile.CreateSystemProfile(
                "Bambu PLA Basic @BBL X1C", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GFA00",
                isSelectable: true, compatibleMachineNames: null, materialType: "PLA", includedTemplateNames: null, sourceVersion: version);
            await filamentRepo.AddAsync(rows[version], CancellationToken.None);
        }

        foreach (var version in baseVersions.Append(catalogVersion).Distinct())
        {
            await machineRepo.AddAsync(MachineProfile.CreateSystemProfile(
                "Bambu Lab P1S 0.4 nozzle", ProfileSourceFormat.OrcaSlicer, null, "{}", "Bambulab", "GM014",
                isSelectable: true, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: version), CancellationToken.None);
        }

        var parser = new FakeProfileBundleParser
        {
            Bundle = new ParsedProfileBundle(
                [], [new ParsedFilamentPreset("Bambu PLA - Red", ProfileSourceFormat.OrcaSlicer, "Bambu PLA Basic @BBL X1C", "{}")]),
        };
        var versions = CatalogVersionsFactory.Create(machineRepo, new SlicerRef(SlicerEngine.OrcaSlicer, catalogVersion),
            CatalogVersionsFactory.Bundled(SlicerEngine.OrcaSlicer, catalogVersion));
        return (new ConfirmProfileImportCommandHandler(parser, machineRepo, filamentRepo, versions), rows);
    }
}
