using FluentAssertions;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using static OpenFarm.Application.Tests.Profiles.Fakes.TwoEngineCatalog;

namespace OpenFarm.Application.Tests.Profiles;

// Whatever the catalogs hold, whatever was selected and whichever slicer is pinned, a preset resolves to the pinned
// slicer's own catalog or is blocked — and the answer never depends on the order rows are stored in.
public class ProfileResolverInvariantTests
{
    private static readonly SlicerRef[] Pins =
    [
        Orca231, Orca242, Bambu271,
        new(SlicerEngine.SuperSlicer, "2.5.59.13"),
        new(SlicerEngine.OrcaSlicer, "9.9.9"),
    ];

    private sealed record Declaration(string Label, Action<TwoEngineCatalog> Declare);

    // One entry per situation the resolver has to tell apart; shuffled per seed so repository order varies.
    private static IReadOnlyList<Declaration> Declarations() =>
    [
        new("common set", c => c.StandardSet(Orca231, Orca242, Bambu271)),
        new("orca-only process", c => c.Process(BambuVendor, "0.16mm Orca Only @BBL X1C", "GPOO1", [MachineName], Orca231, Orca242)),
        new("bambu-only filament", c => c.Filament(BambuVendor, "Bambu Only PLA @BBL X1C", "GFBO1", [MachineName], Bambu271)),
        new("renamed filament", c =>
        {
            c.Filament(BambuVendor, "Old name PLA", "GFSA77", [MachineName], Orca231);
            c.Filament(BambuVendor, "New name PLA", "GFSA77", [MachineName], Orca242);
        }),
        new("stale in bambu", c =>
        {
            c.Filament(BambuVendor, "Stale PLA", "GFST01", [MachineName], Orca231, Orca242);
            c.Filament(BambuVendor, "Stale PLA", "GFST01", [MachineName], Bambu271)[Bambu271].MarkStale();
        }),
        new("ambiguous in orca 2.4.2", c =>
        {
            c.Filament(BambuVendor, "Dup PLA", "GFDU01", [MachineName], Orca231);
            c.Filament(BambuVendor, "Dup PLA (a)", "GFDU01", [MachineName], Orca242);
            c.Filament(BambuVendor, "Dup PLA (b)", "GFDU01", [MachineName], Orca242);
        }),
        new("name keyed", c => c.Filament(BambuVendor, "Name keyed PLA", null, [MachineName], Orca231, Orca242, Bambu271)),
        new("other vendor same id", c =>
        {
            c.Filament("Generic", "Generic PLA", "GFGE01", null, Orca231);
            c.Filament("Other", "Generic PLA", "GFGE01", null, Orca242, Bambu271);
        }),
        new("user presets", c =>
        {
            c.Filaments.Stage(FilamentProfile.Create("My Orca PLA", ProfileSourceFormat.OrcaSlicer, null, """{"filament_type":["PLA"],"hot_plate_temp":["55"]}"""));
            c.Filaments.Stage(FilamentProfile.Create("My Bambu PLA", ProfileSourceFormat.BambuStudio, null, """{"filament_type":["PLA"],"hot_plate_temp":["55"]}"""));
        }),
    ];

    private static TwoEngineCatalog Build(int seed)
    {
        var catalog = new TwoEngineCatalog();
        // Loaded first, so the set of rows (not just their order) is the same for every seed.
        catalog.Loaded(Orca231, Orca242, Bambu271);
        var order = Declarations().ToList();
        if (seed != 0)
        {
            var random = new Random(seed);
            order = order.OrderBy(_ => random.Next()).ToList();
        }

        foreach (var declaration in order)
        {
            declaration.Declare(catalog);
        }

        return catalog;
    }

    private sealed record Outcome(string Kind, string Selected, string Pin, ProfileResolutionOutcome Result, string? Resolved);

    private static string Key(ICatalogProfile row) => $"{row.Name}@{row.Origin}/{row.SourceFormat}/{row.SourceVersion}";

    private static async Task<List<Outcome>> ResolveEverythingAsync(TwoEngineCatalog catalog)
    {
        var outcomes = new List<Outcome>();
        var resolver = catalog.Resolver;
        foreach (var pin in Pins)
        {
            var format = ProfileEngineCompatibility.NativeFormat(pin.Engine);
            void Check<T>(string kind, T selected, ProfileResolution<T> resolution) where T : class, ICatalogProfile
            {
                // INV-1..3: whatever resolved is the pinned engine's own preset for the same logical preset.
                if (resolution.Outcome.IsUsable())
                {
                    var resolved = resolution.Resolved!;
                    resolution.Message.Should().BeNull();
                    resolved.SourceFormat.Should().Be(format, "a usable preset is always in the pinned engine's format");
                    if (selected.Origin == ProfileOrigin.System)
                    {
                        resolved.Origin.Should().Be(ProfileOrigin.System);
                        resolved.SourceVersion.Should().Be(pin.Version, "a System preset always comes from the pinned version's catalog");
                        resolved.VendorName.Should().Be(selected.VendorName);
                        if (selected.ExternalId is not null) resolved.ExternalId.Should().Be(selected.ExternalId);
                        else resolved.Name.Should().Be(selected.Name);
                        resolved.IsCurrentlyOffered.Should().BeTrue();
                        resolved.IsSelectable.Should().BeTrue();
                        if (resolution.Outcome == ProfileResolutionOutcome.MappedEngine) resolved.Name.Should().Be(selected.Name);
                    }
                    else
                    {
                        resolved.Should().BeSameAs(selected, "an imported preset is used as it is, or not at all");
                    }
                }
                else
                {
                    resolution.Resolved.Should().BeNull("a blocked preset has no substitute");
                    resolution.Message.Should().NotBeNullOrWhiteSpace();
                }

                outcomes.Add(new(kind, Key(selected), $"{pin.Engine} {pin.Version}", resolution.Outcome,
                    resolution.Resolved is { } row ? Key(row) : null));
            }

            foreach (var row in catalog.Machines.Rows.ToList()) Check("machine", row, await resolver.ResolveAsync(row, pin, default));
            foreach (var row in catalog.Processes.Rows.ToList()) Check("process", row, await resolver.ResolveAsync(row, pin, default));
            foreach (var row in catalog.Filaments.Rows.ToList()) Check("filament", row, await resolver.ResolveAsync(row, pin, default));
        }

        return outcomes;
    }

    [Fact]
    public async Task EverySelectedRowUnderEveryPin_ResolvesToThePinnedCatalogOrIsBlocked()
    {
        var outcomes = await ResolveEverythingAsync(Build(seed: 0));

        outcomes.Should().NotBeEmpty();
        // The matrix really exercises both worlds: some rows are usable and some are blocked.
        outcomes.Should().Contain(o => o.Result == ProfileResolutionOutcome.MappedEngine)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.MappedVersion)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.Missing)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.Ambiguous)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.NotOffered)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.UnsupportedOrigin)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.CatalogNotLoaded)
            .And.Contain(o => o.Result == ProfileResolutionOutcome.EngineWithoutProfiles);
        outcomes.Where(o => o.Pin.StartsWith("SuperSlicer", StringComparison.Ordinal))
            .Should().OnlyContain(o => o.Result == ProfileResolutionOutcome.EngineWithoutProfiles);
        outcomes.Where(o => o.Pin == "OrcaSlicer 9.9.9").Should().NotContain(o => o.Result.IsUsable() && o.Result != ProfileResolutionOutcome.UserNative);
    }

    [Fact]
    public async Task TheAnswerNeverDependsOnTheOrderRowsAreStoredIn()
    {
        var baseline = (await ResolveEverythingAsync(Build(seed: 0))).OrderBy(o => o.Kind).ThenBy(o => o.Selected).ThenBy(o => o.Pin).ToList();

        for (var seed = 1; seed <= 20; seed++)
        {
            var shuffled = (await ResolveEverythingAsync(Build(seed))).OrderBy(o => o.Kind).ThenBy(o => o.Selected).ThenBy(o => o.Pin).ToList();
            shuffled.Should().Equal(baseline, $"seed {seed} stores the rows in another order");
        }
    }
}
