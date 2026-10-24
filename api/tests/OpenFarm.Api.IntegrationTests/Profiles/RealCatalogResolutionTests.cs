using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Parts;
using Xunit.Abstractions;

namespace OpenFarm.Api.IntegrationTests.Profiles;

/// <summary>Skips unless the real OrcaSlicer 2.3.1 and Bambu Studio 2.7.1 bundles are provisioned (<c>./dev.sh slicers</c>).</summary>
public sealed class ProvisionedSlicerCatalogsFactAttribute : FactAttribute
{
    public ProvisionedSlicerCatalogsFactAttribute()
    {
        if (RealCatalogResolutionTests.LeafOrNull(RealCatalogResolutionTests.Orca) is null || RealCatalogResolutionTests.LeafOrNull(RealCatalogResolutionTests.Bambu) is null)
        {
            Skip = "The OrcaSlicer 2.3.1 and Bambu Studio 2.7.1 bundles are not provisioned under .slicers (./dev.sh slicers).";
        }
    }
}

// The cross-engine twin coverage the strict resolution relies on, measured through the real database: the real catalogs
// of OrcaSlicer 2.3.1 and Bambu Studio 2.7.1 are ingested, then every selectable preset is resolved against the other
// engine with the EF repositories production uses. Infrastructure.Tests' RealSlicerStrictResolutionTests proves the rule
// on the same data in memory; this proves the SQL (identity lookups, null setting_ids, name-keyed duplicates) agrees.
public class RealCatalogResolutionTests(CustomWebApplicationFactory factory, ITestOutputHelper output) : IClassFixture<CustomWebApplicationFactory>
{
    internal static readonly SlicerRef Orca = new(SlicerEngine.OrcaSlicer, "2.3.1");
    internal static readonly SlicerRef Bambu = new(SlicerEngine.BambuStudio, "2.7.1");

    [ProvisionedSlicerCatalogsFact]
    public async Task RealCatalogsInTheDatabase_HaveTheCrossEngineTwinCoverageMeasuredInMemory()
    {
        using var scope = factory.Services.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        await ingestion.IngestAsync(LeafOrNull(Orca)!, CancellationToken.None);
        await ingestion.IngestAsync(LeafOrNull(Bambu)!, CancellationToken.None);
        var resolver = scope.ServiceProvider.GetRequiredService<ProfileResolver>();
        var machines = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processes = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();
        var filaments = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();

        var lines = new List<string>();
        foreach (var (from, to) in new[] { (Orca, Bambu), (Bambu, Orca) })
        {
            var format = ProfileEngineCompatibility.NativeFormat(from.Engine)!.Value;
            var machineRows = (await machines.ListSystemCatalogAsync(format, from.Version, CancellationToken.None)).Where(IsOffered);
            var processRows = (await processes.ListSystemCatalogAsync(format, from.Version, CancellationToken.None)).Where(IsOffered);
            var filamentRows = (await filaments.ListSystemCatalogAsync(format, from.Version, CancellationToken.None)).Where(IsOffered);
            lines.AddRange(await TallyAsync(ProfileKind.Machine, from, to, machineRows, async m => (await resolver.ResolveAsync(m, to, default)).Outcome));
            lines.AddRange(await TallyAsync(ProfileKind.Process, from, to, processRows, async p => (await resolver.ResolveAsync(p, to, default)).Outcome));
            lines.AddRange(await TallyAsync(ProfileKind.Filament, from, to, filamentRows, async f => (await resolver.ResolveAsync(f, to, default)).Outcome));
        }

        foreach (var line in lines) output.WriteLine(line);
        lines.Should().Equal(ExpectedCoverage, "the database must resolve the real catalogs exactly as the in-memory repositories do (see RealSlicerStrictResolutionTests)");
    }

    // The same numbers as Infrastructure.Tests' RealSlicerStrictResolutionTests.ExpectedCoverage.
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

    /// <summary>The provisioned bundle leaf (<c>.slicers/&lt;engine&gt;/&lt;version&gt;/&lt;platform&gt;</c>), or <c>null</c> when it is not there.</summary>
    internal static string? LeafOrNull(SlicerRef slicer)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "slicers.manifest.json"))) dir = dir.Parent;
        if (dir is null) return null;
        var engine = slicer.Engine == SlicerEngine.BambuStudio ? "bambustudio" : "orcaslicer";
        var platform = new HostPlatform().Current switch
        {
            SlicerPlatform.Linux => "linux",
            SlicerPlatform.Windows => "windows",
            _ => "macos",
        };
        var leaf = Path.Combine(dir.FullName, ".slicers", engine, slicer.Version, platform);
        return Directory.Exists(leaf) ? leaf : null;
    }
}
