using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;
using OpenFarm.Infrastructure.Tests.Parts.RealSlicer;
using Xunit.Abstractions;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Production resolves what the operator selected to the pinned slicer's own copy of it. That leans on a fact about real
// data: OrcaSlicer and Bambu Studio ship the same Bambu Lab preset under the same setting_id, vendor and name. These
// tests run the real resolver over the real, locally provisioned catalogs (and the real Orca CLI), so they self-skip
// where no bundle is installed (RealSlicerFactAttribute) — see Parts/Fixtures/RealSlicer/README.md.
public class RealSlicerStrictResolutionTests(ITestOutputHelper output)
{
    private static readonly SlicerRef Orca = new(SlicerEngine.OrcaSlicer, "2.3.1");
    private static readonly SlicerRef Bambu = new(SlicerEngine.BambuStudio, "2.7.1");
    private static readonly string BenchyCase = Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "RealSlicer", "orcaslicer-2.3.1-benchy-stl");

    // Ingesting a real catalog takes seconds; the rows are only read after this, so every test shares one staging.
    private static readonly Lazy<Task<StagedCatalogs>> Staged =
        new(() => VendorProfileFixtureStaging.StageCatalogsAsync([Orca, Bambu], CancellationToken.None));

    // The operator selected Bambu Studio's copy of the X1C-labelled P1S presets; the part is pinned to Orca. The G-code
    // must be the one Orca makes from its own copies — same rows, same settings, same toolpath.
    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1", SlicerEngine.BambuStudio, "2.7.1")]
    public async Task BambuStudioSelection_PinnedToOrca_SlicesLikeTheOrcaSelection()
    {
        var (orcaCli, reason) = RealSlicerBundleLocator.TryResolve(Orca.Engine, Orca.Version);
        orcaCli.Should().NotBeNull(reason);
        var manifest = GoldenCaseManifest.Load(BenchyCase);
        var catalogs = await Staged.Value;

        var native = await ResolveAsync(catalogs, selectedIn: Orca, manifest);
        var mapped = await ResolveAsync(catalogs, selectedIn: Bambu, manifest);

        new[] { native.Machine, native.Process, native.Filament }.Select(p => p.Outcome)
            .Should().OnlyContain(o => o == ProfileResolutionOutcome.Exact);
        new[] { mapped.Machine, mapped.Process, mapped.Filament }.Select(p => p.Outcome)
            .Should().OnlyContain(o => o == ProfileResolutionOutcome.MappedEngine);
        mapped.Pin.Should().Be(Orca);
        (mapped.Machine.ResolvedId, mapped.Process.ResolvedId, mapped.Filament.ResolvedId)
            .Should().Be((native.Machine.ResolvedId, native.Process.ResolvedId, native.Filament.ResolvedId), "Orca's own rows are used, not Bambu Studio's");
        (mapped.Machine.SelectedId, mapped.Process.SelectedId, mapped.Filament.SelectedId)
            .Should().NotBe((mapped.Machine.ResolvedId, mapped.Process.ResolvedId, mapped.Filament.ResolvedId), "the operator selected the other engine's rows");
        new[] { mapped.Machine, mapped.Process, mapped.Filament }.Should().OnlyContain(p => p.Format == ProfileSourceFormat.OrcaSlicer && p.SourceVersion == Orca.Version);
        (mapped.MachineSettingsJson, mapped.ProcessSettingsJson, mapped.FilamentSettingsJson)
            .Should().Be((native.MachineSettingsJson, native.ProcessSettingsJson, native.FilamentSettingsJson));

        var model = await File.ReadAllBytesAsync(Path.Combine(BenchyCase, manifest.ModelFileName));
        var nativeGCode = await SliceAsync(orcaCli!, model, manifest, native);
        var mappedGCode = await SliceAsync(orcaCli!, model, manifest, mapped);
        var diff = GCodeNormalizer.Diff(GCodeNormalizer.Normalize(nativeGCode), GCodeNormalizer.Normalize(mappedGCode));
        diff.IsMatch.Should().BeTrue($"selecting Bambu Studio's presets must slice exactly like selecting Orca's:\n{diff.Message}");
    }

    // 'Bambu PLA Basic @BBL P1S 0.4 nozzle' (setting_id GFSA00_34) ships with Bambu Studio 2.7.1 and not with OrcaSlicer
    // 2.3.1. A part pinned to Orca must be refused with that name — never sliced with a neighbouring filament, and the
    // refusal comes from the resolver, before any slicer process exists.
    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1", SlicerEngine.BambuStudio, "2.7.1")]
    public async Task FilamentOnlyBambuStudioShips_PinnedToOrca_IsBlockedByName()
    {
        const string FilamentName = "Bambu PLA Basic @BBL P1S 0.4 nozzle";
        var catalogs = await Staged.Value;
        var manifest = GoldenCaseManifest.Load(BenchyCase);
        var filament = await catalogs.FilamentAsync(Bambu, FilamentName);
        (await catalogs.FilamentsAsync(Orca)).Should().NotContain(f => f.Name == FilamentName, "that is what makes it a real missing-twin case");

        var machine = await catalogs.MachineAsync(Bambu, manifest.MachineProfileName!);
        var process = await catalogs.ProcessAsync(Bambu, manifest.ProcessProfileName!);

        var act = () => catalogs.Resolver.ResolveProfilesAsync(
            machine.Id, process.Id, filament.Id, BuildPlateType.TexturedPei, Orca, use: null, CancellationToken.None);

        var blocked = (await act.Should().ThrowAsync<ProfileSetBlockedException>()).Which;
        blocked.Issues.Should().ContainSingle().Which.Should().Match<ProfileIssue>(i =>
            i.Kind == ProfileKind.Filament && i.Code == ProfileResolutionOutcome.Missing && i.Name == FilamentName);
        blocked.Message.Should().Contain($"Filament profile '{FilamentName}' is not in OrcaSlicer 2.3.1");
    }

    // The coverage the design relies on, measured on the real catalogs: how many selectable presets of one engine have an
    // exact twin in the other. A bundle bump that changes it fails here, so the change is looked at before it reaches an
    // operator as a blocked job. Update the numbers deliberately.
    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1", SlicerEngine.BambuStudio, "2.7.1")]
    public async Task RealCatalogs_CrossEngineTwinCoverage_IsWhatItWasMeasuredToBe()
    {
        var catalogs = await Staged.Value;
        var lines = new List<string>();
        foreach (var (from, to) in new[] { (Orca, Bambu), (Bambu, Orca) })
        {
            var machines = (await catalogs.MachinesAsync(from)).Where(IsOffered);
            var processes = (await catalogs.ProcessesAsync(from)).Where(IsOffered);
            var filaments = (await catalogs.FilamentsAsync(from)).Where(IsOffered);
            lines.AddRange(await TallyAsync(ProfileKind.Machine, from, to, machines, async m => (await catalogs.Profiles.ResolveAsync(m, to, default)).Outcome));
            lines.AddRange(await TallyAsync(ProfileKind.Process, from, to, processes, async p => (await catalogs.Profiles.ResolveAsync(p, to, default)).Outcome));
            lines.AddRange(await TallyAsync(ProfileKind.Filament, from, to, filaments, async f => (await catalogs.Profiles.ResolveAsync(f, to, default)).Outcome));
        }

        foreach (var line in lines) output.WriteLine(line);
        lines.Should().Equal(ExpectedCoverage, "the real catalogs' cross-engine coverage changed — review what that means for pinned parts");
    }

    // Measured with the OrcaSlicer 2.3.1 and Bambu Studio 2.7.1 bundles of slicers.manifest.json. Every selectable Orca Bambu
    // machine and all but one Orca Bambu process has a Bambu Studio twin; 275 Orca Bambu filaments have none. The Snapmaker vendor
    // (ingested for the U1) is Orca-only: its 73 machines, 83 processes and 114 filaments have no Bambu Studio twin, so a U1 part
    // must stay pinned to Orca. The other way round, Bambu Studio ships 28 machines, 113 processes and 1202 filaments that Orca's
    // catalog lacks — a part pinned to Orca cannot use those.
    private static readonly string[] ExpectedCoverage =
    [
        "Machine OrcaSlicer 2.3.1 -> BambuStudio 2.7.1: MappedEngine 28",
        "Machine OrcaSlicer 2.3.1 -> BambuStudio 2.7.1: Missing 73",
        "Process OrcaSlicer 2.3.1 -> BambuStudio 2.7.1: MappedEngine 118",
        "Process OrcaSlicer 2.3.1 -> BambuStudio 2.7.1: Missing 84",
        "Filament OrcaSlicer 2.3.1 -> BambuStudio 2.7.1: MappedEngine 552",
        "Filament OrcaSlicer 2.3.1 -> BambuStudio 2.7.1: Missing 389",
        "Machine BambuStudio 2.7.1 -> OrcaSlicer 2.3.1: MappedEngine 28",
        "Machine BambuStudio 2.7.1 -> OrcaSlicer 2.3.1: Missing 28",
        "Process BambuStudio 2.7.1 -> OrcaSlicer 2.3.1: MappedEngine 118",
        "Process BambuStudio 2.7.1 -> OrcaSlicer 2.3.1: Missing 113",
        "Filament BambuStudio 2.7.1 -> OrcaSlicer 2.3.1: MappedEngine 552",
        "Filament BambuStudio 2.7.1 -> OrcaSlicer 2.3.1: Missing 1202",
    ];

    private static bool IsOffered(ICatalogProfile row) => row.IsSelectable && row.IsCurrentlyOffered;

    private static async Task<IEnumerable<string>> TallyAsync<TRow>(
        ProfileKind kind, SlicerRef from, SlicerRef to, IEnumerable<TRow> rows, Func<TRow, Task<ProfileResolutionOutcome>> resolve)
    {
        var outcomes = new List<ProfileResolutionOutcome>();
        foreach (var row in rows) outcomes.Add(await resolve(row));
        return outcomes.GroupBy(o => o).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .Select(g => $"{kind} {from.Engine} {from.Version} -> {to.Engine} {to.Version}: {g.Key} {g.Count()}");
    }

    private static async Task<ResolvedSlicingProfiles> ResolveAsync(StagedCatalogs catalogs, SlicerRef selectedIn, GoldenCaseManifest manifest) =>
        await catalogs.Resolver.ResolveProfilesAsync(
            (await catalogs.MachineAsync(selectedIn, manifest.MachineProfileName!)).Id,
            (await catalogs.ProcessAsync(selectedIn, manifest.ProcessProfileName!)).Id,
            (await catalogs.FilamentAsync(selectedIn, manifest.FilamentProfileName!)).Id,
            BuildPlateType.TexturedPei, Orca, use: null, CancellationToken.None);

    private static Task<string> SliceAsync(AvailableSlicer orcaCli, byte[] model, GoldenCaseManifest manifest, ResolvedSlicingProfiles profiles) =>
        RealSlicerProcessRunner.SliceAsync(orcaCli, new PrusaSliceFamilyInvocation(), model, manifest.ModelFileFormat,
            manifest.MaterialType, manifest.NozzleDiameterMm, profiles, CancellationToken.None);
}
