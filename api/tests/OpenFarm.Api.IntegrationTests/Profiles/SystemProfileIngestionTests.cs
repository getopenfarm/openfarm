using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// Integration coverage for System Profile ingestion (009, contracts/system-profile-ingestion.md) —
// runs the real ISystemProfileIngestionService against a staged bundle leaf built from the real
// vendor JSON fixtures VendorProfileIndexReaderTests already established (see that project's
// Fixtures/VendorIndex/README.md), linked into this project rather than duplicated.
//
// Each test stages its own bundle leaf with a uniquely-renamed vendor "name" (the fixture's own
// preset content, machine model names, and setting_ids are left untouched — only the vendor index's
// top-level identity is rewritten) so tests never collide on the shared (single-organization)
// database the class fixture's Postgres container backs, regardless of xUnit's execution order.
public class SystemProfileIngestionTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Profiles", "Fixtures", "VendorIndex");

    [Fact]
    public async Task IngestAsync_OrcaSlicerFixtureAlongsideUnsupportedVendor_CreatesSystemRowsOnlyForTheSupportedVendor()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var leaf = StageLeaf(("OrcaSlicer", vendorName), ("UnsupportedVendor", null));

        using var scope = factory.Services.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        var machineRepo = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processRepo = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();
        var filamentRepo = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();

        var summary = await ingestionService.IngestAsync(leaf, CancellationToken.None);

        summary.VendorsScanned.Should().Be(1);
        summary.VendorsSkipped.Should().Be(1);

        var machineRows = await machineRepo.ListAsync(CancellationToken.None);
        var leafMachine = machineRows.Should().ContainSingle(p => p.VendorName == vendorName && p.ExternalId == "GM001").Subject;
        leafMachine.Origin.Should().Be(ProfileOrigin.System);
        leafMachine.BaseProfileId.Should().NotBeNull("its abstract-base ancestor was ingested in the same run");

        var processRows = await processRepo.ListAsync(CancellationToken.None);
        processRows.Should().ContainSingle(p => p.VendorName == vendorName && p.ExternalId == "GP004");

        var filamentRows = await filamentRepo.ListAsync(CancellationToken.None);
        var filamentLeaf = filamentRows.Should().ContainSingle(p => p.VendorName == vendorName && p.ExternalId == "GFSA00").Subject;
        filamentLeaf.MaterialType.Should().Be("PLA");

        machineRows.Should().NotContain(p => p.VendorName == "Creality");
        processRows.Should().NotContain(p => p.VendorName == "Creality");
        filamentRows.Should().NotContain(p => p.VendorName == "Creality");
    }

    [Fact]
    public async Task IngestAsync_RerunAgainstUnchangedData_ReportsZeroCreatedOrUpdated()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var leaf = StageLeaf(("OrcaSlicer", vendorName));

        using var scope = factory.Services.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();

        var first = await ingestionService.IngestAsync(leaf, CancellationToken.None);
        first.Machine.Created.Should().BeGreaterThan(0);

        var second = await ingestionService.IngestAsync(leaf, CancellationToken.None);

        second.Machine.Created.Should().Be(0);
        second.Machine.Updated.Should().Be(0);
        second.Process.Created.Should().Be(0);
        second.Process.Updated.Should().Be(0);
        second.Filament.Created.Should().Be(0);
        second.Filament.Updated.Should().Be(0);
    }

    [Fact]
    public async Task IngestAsync_RenamedLeafBetweenRuns_UpdatesTheSameRowInPlaceRatherThanDuplicating()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var leaf = StageLeaf(("OrcaSlicer", vendorName));

        using var scope = factory.Services.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        var processRepo = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();

        await ingestionService.IngestAsync(leaf, CancellationToken.None);
        var beforeRename = (await processRepo.ListAsync(CancellationToken.None))
            .Single(p => p.VendorName == vendorName && p.ExternalId == "GP004");
        var originalId = beforeRename.Id;
        beforeRename.Name.Should().Be("0.20mm Standard @BBL X1C");

        // Rename the leaf's own Name in place (setting_id GP004 stays the same) — mirrors a real
        // vendor rename between slicer versions (research.md §1's renamed_from case).
        var processFilePath = Path.Combine(leaf, "resources", "profiles", "process", "0.20mm Standard @BBL X1C.json");
        var renamedJson = File.ReadAllText(processFilePath).Replace(
            "\"name\": \"0.20mm Standard @BBL X1C\"", "\"name\": \"0.20mm Renamed @BBL X1C\"");
        File.WriteAllText(processFilePath, renamedJson);

        await ingestionService.IngestAsync(leaf, CancellationToken.None);

        var allProcessRows = await processRepo.ListAsync(CancellationToken.None);
        allProcessRows.Should().NotContain(p => p.VendorName == vendorName && p.Name == "0.20mm Standard @BBL X1C");
        var afterRename = allProcessRows.Should().ContainSingle(p => p.VendorName == vendorName && p.ExternalId == "GP004").Subject;
        afterRename.Id.Should().Be(originalId);
        afterRename.Name.Should().Be("0.20mm Renamed @BBL X1C");
    }

    [Fact]
    public async Task IngestAsync_BambuStudioFixture_NeverCreatesRowsFromUntypedIncludeTargetFragmentsAndResolvesIncludedTemplateNames()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var leaf = StageLeaf(("BambuStudio", vendorName));

        using var scope = factory.Services.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        var machineRepo = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();

        await ingestionService.IngestAsync(leaf, CancellationToken.None);

        var machineRows = await machineRepo.ListAsync(CancellationToken.None);
        var fragment = machineRows.Should().ContainSingle(
            p => p.VendorName == vendorName && p.Name == "Bambu Lab X1 Carbon 0.4 nozzle template machine_start_gcode").Subject;
        fragment.IsSelectable.Should().BeFalse();

        var leafMachine = machineRows.Should().ContainSingle(
            p => p.VendorName == vendorName && p.Name == "Bambu Lab X1 Carbon 0.4 nozzle").Subject;
        leafMachine.IsSelectable.Should().BeTrue();
        leafMachine.IncludedTemplateNames.Should().NotBeNull();
        leafMachine.IncludedTemplateNames!.Should().Contain("Bambu Lab X1 Carbon 0.4 nozzle template machine_start_gcode");
    }

    /// <summary>
    /// Copies one or more of this project's linked VendorIndex fixture folders into a fresh temp
    /// directory shaped like a real bundle leaf (<c>&lt;root&gt;/orcaslicer/&lt;version&gt;/linux/resources/profiles/…</c>,
    /// matching <see cref="OpenFarm.Infrastructure.Parts.BundledSlicerScanner"/>'s own convention),
    /// optionally rewriting each fixture's vendor index top-level <c>"name"</c> to a unique value for
    /// test isolation (machine model names / setting_ids / preset content are left untouched).
    /// </summary>
    private static string StageLeaf(params (string FixtureFolder, string? RenamedVendorName)[] vendors)
    {
        var engineSlug = vendors.Any(v => v.FixtureFolder == "BambuStudio") ? "bambustudio" : "orcaslicer";
        var leaf = Path.Combine(Path.GetTempPath(), "openfarm-ingestion-tests", Guid.NewGuid().ToString(), engineSlug, "1.0.0", "linux");
        var profilesDir = Path.Combine(leaf, "resources", "profiles");
        Directory.CreateDirectory(profilesDir);

        foreach (var (fixtureFolder, renamedVendorName) in vendors)
        {
            var sourceDir = Path.Combine(FixturesRoot, fixtureFolder);
            CopyDirectoryContents(sourceDir, profilesDir);

            if (renamedVendorName is not null)
            {
                RenameVendorIndexName(profilesDir, renamedVendorName);
            }
        }

        return leaf;
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            Directory.CreateDirectory(destSubDir);
            CopyDirectoryContents(subDir, destSubDir);
        }
    }

    // The one vendor index file this fixture folder contributes is always its single top-level *.json
    // file (BBL.json / Creality.json) — rewritten in place after copying.
    private static void RenameVendorIndexName(string profilesDir, string newVendorName)
    {
        foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var json = File.ReadAllText(file);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("machine_model_list", out _))
            {
                continue;
            }

            // Only ever one un-renamed vendor index at a time per StageLeaf call for a given engine —
            // rewrite whichever one hasn't already been claimed by an earlier (FixtureFolder, name) pair.
            var currentName = document.RootElement.GetProperty("name").GetString();
            if (currentName is null || currentName.StartsWith("Bambulab-", StringComparison.Ordinal))
            {
                continue;
            }

            var rewritten = json.Replace($"\"name\": \"{currentName}\"", $"\"name\": \"{newVendorName}\"");
            File.WriteAllText(file, rewritten);
        }
    }
}
