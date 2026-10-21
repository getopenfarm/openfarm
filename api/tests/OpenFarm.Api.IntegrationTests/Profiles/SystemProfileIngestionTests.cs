using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.FilamentTracking;
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
// top-level identity is rewritten) so tests never collide on the shared
// database the class fixture's Postgres container backs, regardless of xUnit's execution order.
public class SystemProfileIngestionTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Profiles", "Fixtures", "VendorIndex");

    [Fact]
    public async Task IngestAsync_OrcaSlicerFixtureAlongsideUnsupportedVendor_CreatesSystemRowsOnlyForTheSupportedVendor()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var filamentVendorSuffix = Guid.NewGuid().ToString("N");
        var leaf = StageLeaf(filamentVendorSuffix, ("OrcaSlicer", vendorName), ("UnsupportedVendor", null));

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

        // Filament rows are keyed by their own resolved filament_vendor, NOT the vendor index's own
        // (renamed) name — "Bambu PLA Basic @BBL X1C" resolves to its "Bambu Lab" ancestor override,
        // never the outer "Bambulab-..." index name (this is the "Generic imported as Bambulab" bug fix).
        var filamentRows = await filamentRepo.ListAsync(CancellationToken.None);
        var filamentLeaf = filamentRows.Should().ContainSingle(
            p => p.ExternalId == "GFSA00" && p.VendorName == $"Bambu Lab-{filamentVendorSuffix}").Subject;
        filamentLeaf.VendorName.Should().NotBe(vendorName);
        filamentLeaf.MaterialType.Should().Be("PLA");

        machineRows.Should().NotContain(p => p.VendorName == "Creality");
        processRows.Should().NotContain(p => p.VendorName == "Creality");
        filamentRows.Should().NotContain(p => p.VendorName == "Creality");
    }

    [Fact]
    public async Task IngestAsync_OrcaSlicerFixture_GenericAncestorRowResolvesToGenericVendorAndCreatesMatchingVendorRows()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var filamentVendorSuffix = Guid.NewGuid().ToString("N");
        var leaf = StageLeaf(filamentVendorSuffix, ("OrcaSlicer", vendorName));

        using var scope = factory.Services.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        var filamentRepo = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();
        var vendorRepo = scope.ServiceProvider.GetRequiredService<IVendorRepository>();

        await ingestionService.IngestAsync(leaf, CancellationToken.None);

        var genericVendorName = $"Generic-{filamentVendorSuffix}";
        var brandedVendorName = $"Bambu Lab-{filamentVendorSuffix}";

        // fdm_filament_common is the material-family root every filament in this fixture ultimately
        // inherits from, and declares filament_vendor "Generic" directly — it must resolve to that,
        // never the vendor index's own "Bambulab-..." name.
        var filamentRows = await filamentRepo.ListAsync(CancellationToken.None);
        filamentRows.Should().ContainSingle(p => p.Name == "fdm_filament_common" && p.VendorName == genericVendorName);

        // Both real brands seen during ingestion get their own Vendor row — the profile is tied to a
        // vendor, so ingesting it makes that brand available for logging physical filament stock too.
        (await vendorRepo.GetByNameAsync(genericVendorName, CancellationToken.None)).Should().NotBeNull();
        (await vendorRepo.GetByNameAsync(brandedVendorName, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task IngestingBothEnginesPreservesSeparateIdentitiesInheritanceAndAvailability()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var orcaLeaf = StageLeaf(suffix, ("OrcaSlicer", vendor));
        var bambuLeaf = StageLeaf(suffix, ("BambuStudio", vendor));
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        var machines = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processes = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();
        var filaments = scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>();
        await service.IngestAsync(orcaLeaf, default);
        await service.IngestAsync(bambuLeaf, default);
        var unchanged = await service.IngestAsync(orcaLeaf, default);
        unchanged.Machine.Created.Should().Be(0);
        unchanged.Process.Created.Should().Be(0);
        unchanged.Filament.Created.Should().Be(0);
        foreach (var format in new[] { ProfileSourceFormat.OrcaSlicer, ProfileSourceFormat.BambuStudio })
        {
            var machine = (await machines.ListAsync(default)).SingleOrDefault(
                p => p.SourceFormat == format && p.VendorName == vendor && p.ExternalId == "GM001");
            machine.Should().NotBeNull();
            machine!.IsCurrentlyOffered.Should().BeTrue();
            machine.BaseProfileId.Should().NotBeNull();
            (await machines.GetByIdAsync(machine.BaseProfileId!.Value, default))!.SourceFormat.Should().Be(format);
            var process = (await processes.ListAsync(default)).SingleOrDefault(
                p => p.SourceFormat == format && p.VendorName == vendor && p.ExternalId == "GP004");
            process.Should().NotBeNull();
            process!.IsCurrentlyOffered.Should().BeTrue();
            var filament = (await filaments.ListAsync(default)).SingleOrDefault(
                p => p.SourceFormat == format && p.VendorName == $"Bambu Lab-{suffix}" && p.ExternalId == "GFSA00");
            filament.Should().NotBeNull();
            filament!.IsCurrentlyOffered.Should().BeTrue();
            (await filaments.GetByIdAsync(filament.BaseProfileId!.Value, default))!.SourceFormat.Should().Be(format);
        }
        // A fresh scope verifies re-ingestion did not persist stale flags on the other catalog.
        using var fresh = factory.Services.CreateScope();
        var persisted = await fresh.ServiceProvider.GetRequiredService<IMachineProfileRepository>().ListAsync(default);
        persisted.Where(p => p.VendorName == vendor).Should().OnlyContain(p => p.IsCurrentlyOffered);
    }

    [Fact]
    public async Task IngestAsync_RerunAgainstUnchangedData_ReportsZeroCreatedOrUpdated()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var leaf = StageLeaf(Guid.NewGuid().ToString("N"), ("OrcaSlicer", vendorName));

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
        var leaf = StageLeaf(Guid.NewGuid().ToString("N"), ("OrcaSlicer", vendorName));

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
        var leaf = StageLeaf(Guid.NewGuid().ToString("N"), ("BambuStudio", vendorName));

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

    [Fact]
    public async Task IngestAsync_TwoVersionsOfOneEngine_KeepSeparateCatalogsThatNeverOverwriteEachOther()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var v1 = StageLeafAt("1.0.0", suffix, ("OrcaSlicer", vendor));
        var v2 = StageLeafAt("2.0.0", suffix, ("OrcaSlicer", vendor));
        EditFixture(v2, "process/0.20mm Standard @BBL X1C.json", "It has a general layer height", "NEW-IN-2.0.0 It has a general layer height");

        IngestionSummary first, second;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
            first = await service.IngestAsync(v1, default);
            second = await service.IngestAsync(v2, default);
        }

        // The second version is a fresh catalog: nothing of it counts as "updated" and nothing of the first changes.
        second.Machine.Created.Should().Be(first.Machine.Created);
        second.Process.Created.Should().Be(first.Process.Created);
        second.Filament.Created.Should().Be(first.Filament.Created);
        second.Process.Updated.Should().Be(0);

        using var fresh = factory.Services.CreateScope();
        var processes = (await fresh.ServiceProvider.GetRequiredService<IProcessProfileRepository>().ListAsync(default))
            .Where(p => p.VendorName == vendor && p.ExternalId == "GP004").ToList();
        processes.Should().HaveCount(2);
        processes.Single(p => p.SourceVersion == "1.0.0").RawDataJson.Should().NotContain("NEW-IN-2.0.0");
        processes.Single(p => p.SourceVersion == "2.0.0").RawDataJson.Should().Contain("NEW-IN-2.0.0");

        var machines = (await fresh.ServiceProvider.GetRequiredService<IMachineProfileRepository>().ListAsync(default))
            .Where(p => p.VendorName == vendor).ToList();
        var leafMachine = machines.Single(p => p.SourceVersion == "2.0.0" && p.ExternalId == "GM001");
        machines.Single(p => p.Id == leafMachine.BaseProfileId).SourceVersion.Should().Be("2.0.0", "inheritance never crosses catalog versions");
    }

    [Fact]
    public async Task IngestAsync_RemovedPreset_IsStaleOnlyInTheVersionThatDroppedIt()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var v1 = StageLeafAt("1.0.0", suffix, ("OrcaSlicer", vendor));
        var v2 = StageLeafAt("2.0.0", suffix, ("OrcaSlicer", vendor));
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
            await service.IngestAsync(v1, default);
            await service.IngestAsync(v2, default);
        }

        RemoveFromIndex(v2, "process_list", "0.20mm Standard @BBL X1C");
        IngestionSummary rerun;
        using (var scope = factory.Services.CreateScope())
        {
            rerun = await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(v2, default);
        }

        rerun.Process.MarkedStale.Should().Be(1);
        // Read from a fresh DbContext so the assertion sees what was persisted, not what the ingestion run tracked.
        using var fresh = factory.Services.CreateScope();
        var processes = (await fresh.ServiceProvider.GetRequiredService<IProcessProfileRepository>().ListAsync(default))
            .Where(p => p.VendorName == vendor && p.ExternalId == "GP004").ToList();
        processes.Single(p => p.SourceVersion == "2.0.0").IsCurrentlyOffered.Should().BeFalse();
        processes.Single(p => p.SourceVersion == "1.0.0").IsCurrentlyOffered.Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_RerunInAFreshProcess_ReportsNothingCreatedOrUpdated()
    {
        // A new DbContext reads the rows back as Postgres jsonb text (its own key order and spacing), so this
        // only stays at zero if change detection compares JSON structurally rather than as text.
        var leaf = StageLeaf(Guid.NewGuid().ToString("N"), ("OrcaSlicer", $"Bambulab-{Guid.NewGuid():N}"));
        using (var first = factory.Services.CreateScope())
        {
            await first.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        using var second = factory.Services.CreateScope();
        var rerun = await second.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);

        rerun.Machine.Should().Be(new ProfileKindIngestionCounts(0, 0, 0));
        rerun.Process.Should().Be(new ProfileKindIngestionCounts(0, 0, 0));
        rerun.Filament.Should().Be(new ProfileKindIngestionCounts(0, 0, 0));
    }

    [Fact]
    public async Task IngestAsync_PresetsSharingASettingId_AreKeyedByNameAndNoneIsLost()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var leaf = StageLeaf(suffix, ("OrcaSlicer", vendor));
        // Two more presets with the same setting_id (GFSA00) as "Bambu PLA Basic @BBL X1C" — as in the real BBL bundle.
        var original = File.ReadAllText(Path.Combine(leaf, "resources", "profiles", "filament", "Bambu PLA Basic @BBL X1C.json"));
        foreach (var name in new[] { "Bambu PLA Basic Dup A", "Bambu PLA Basic Dup B" })
        {
            File.WriteAllText(
                Path.Combine(leaf, "resources", "profiles", "filament", $"{name}.json"),
                original.Replace("\"name\": \"Bambu PLA Basic @BBL X1C\"", $"\"name\": \"{name}\""));
            AddToIndex(leaf, "filament_list", name, $"filament/{name}.json");
        }

        IngestionSummary first, second;
        using (var scope = factory.Services.CreateScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        using (var scope = factory.Services.CreateScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        using var fresh = factory.Services.CreateScope();
        var rows = (await fresh.ServiceProvider.GetRequiredService<IFilamentProfileRepository>().ListAsync(default))
            .Where(p => p.VendorName == $"Bambu Lab-{suffix}" && p.Name.StartsWith("Bambu PLA Basic ", StringComparison.Ordinal) && p.IsSelectable).ToList();
        rows.Select(p => p.Name).Should().BeEquivalentTo("Bambu PLA Basic @BBL X1C", "Bambu PLA Basic Dup A", "Bambu PLA Basic Dup B");
        rows.Should().OnlyContain(p => p.ExternalId == null, "a shared setting_id cannot be the identity");
        second.Filament.Created.Should().Be(0);
        second.Filament.Updated.Should().Be(0);
        first.Filament.Created.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IngestAsync_BaseRemovedBetweenRuns_ClearsTheStoredBaseLink()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var leaf = StageLeaf(suffix, ("OrcaSlicer", vendor));
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        // The preset now inherits from a name this vendor no longer ships. (A process preset, because a filament's
        // resolved brand comes from its ancestor chain: a broken chain would change its identity, not just its base.)
        EditFixture(leaf, "process/0.20mm Standard @BBL X1C.json", "\"inherits\": \"fdm_process_single_0.20\"", "\"inherits\": \"Gone Base\"");
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        using var fresh = factory.Services.CreateScope();
        var row = (await fresh.ServiceProvider.GetRequiredService<IProcessProfileRepository>().ListAsync(default))
            .Single(p => p.VendorName == vendor && p.ExternalId == "GP004");
        row.BaseProfileName.Should().Be("Gone Base");
        row.BaseProfileId.Should().BeNull("a link to the previous parent row would resolve a base the preset no longer inherits");
    }

    [Fact]
    public async Task IngestAsync_FilamentInheritingFromTheOrcaLibrary_StoresOnlyTheLibraryBasesItNeedsAndResolvesTheChain()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var leaf = StageLeaf(suffix, ("OrcaSlicer", vendor));
        var profiles = Path.Combine(leaf, "resources", "profiles");

        // OrcaSlicer ships generic bases in a vendor-less index of its own; the vendor's filament inherits from it.
        var library = new[]
        {
            ("fdm_filament_common", """{"type":"filament","name":"fdm_filament_common","instantiation":"false","filament_vendor":["Generic"],"x_origin":"LIBRARY"}"""),
            ("fdm_filament_pla", """{"type":"filament","name":"fdm_filament_pla","inherits":"fdm_filament_common","instantiation":"false","filament_type":["PLA"],"x_origin":"LIBRARY"}"""),
            ("Overture PLA @base", $$"""{"type":"filament","name":"Overture PLA @base","inherits":"fdm_filament_pla","instantiation":"false","filament_vendor":["Overture-{{suffix}}"]}"""),
            ("Unrelated PETG @base", """{"type":"filament","name":"Unrelated PETG @base","inherits":"fdm_filament_common","instantiation":"false","filament_type":["PETG"]}"""),
        };
        Directory.CreateDirectory(Path.Combine(profiles, "OrcaFilamentLibrary", "filament"));
        foreach (var (name, json) in library)
        {
            File.WriteAllText(Path.Combine(profiles, "OrcaFilamentLibrary", "filament", $"{name}.json"), json);
        }

        File.WriteAllText(
            Path.Combine(profiles, "OrcaFilamentLibrary.json"),
            new JsonObject
            {
                ["name"] = "OrcaFilamentLibrary",
                ["filament_list"] = new JsonArray(library.Select(p => (JsonNode)new JsonObject { ["name"] = p.Item1, ["sub_path"] = $"filament/{p.Item1}.json" }).ToArray()),
            }.ToJsonString());
        File.WriteAllText(
            Path.Combine(profiles, "filament", "Overture PLA @BBL X1C.json"),
            """{"type":"filament","name":"Overture PLA @BBL X1C","inherits":"Overture PLA @base","from":"system","setting_id":"OVR01","instantiation":"true"}""");
        AddToIndex(leaf, "filament_list", "Overture PLA @BBL X1C", "filament/Overture PLA @BBL X1C.json");

        IngestionSummary second;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        using (var scope = factory.Services.CreateScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>().IngestAsync(leaf, default);
        }

        using var fresh = factory.Services.CreateScope();
        var rows = (await fresh.ServiceProvider.GetRequiredService<IFilamentProfileRepository>().ListAsync(default)).ToList();
        var leafRow = rows.Single(p => p.ExternalId == "OVR01" && p.VendorName == $"Overture-{suffix}");
        leafRow.MaterialType.Should().Be("PLA", "the chain through the library declares filament_type");
        leafRow.IsSelectable.Should().BeTrue();

        var overtureBase = rows.Single(p => p.Id == leafRow.BaseProfileId);
        overtureBase.Name.Should().Be("Overture PLA @base");
        overtureBase.VendorName.Should().Be("OrcaFilamentLibrary");
        overtureBase.IsSelectable.Should().BeFalse("library bases are never offered as presets");
        var plaBase = rows.Single(p => p.Id == overtureBase.BaseProfileId);
        plaBase.VendorName.Should().Be("OrcaFilamentLibrary");
        plaBase.RawDataJson.Should().Contain("LIBRARY", "a library preset's parent is the library's own, not the vendor's same-named preset");
        rows.Single(p => p.Id == plaBase.BaseProfileId).Name.Should().Be("fdm_filament_common");

        rows.Should().NotContain(p => p.Name == "Unrelated PETG @base", "only the library bases a vendor preset needs are stored");
        var vendors = fresh.ServiceProvider.GetRequiredService<IVendorRepository>();
        (await vendors.GetByNameAsync($"Overture-{suffix}", default)).Should().NotBeNull();
        (await vendors.GetByNameAsync("OrcaFilamentLibrary", default)).Should().BeNull();

        second.Filament.Created.Should().Be(0);
        second.Filament.Updated.Should().Be(0);
    }

    private static void EditFixture(string leaf, string relativePath, string oldText, string newText)
    {
        var path = Path.Combine(leaf, "resources", "profiles", relativePath);
        var text = File.ReadAllText(path);
        text.Should().Contain(oldText);
        File.WriteAllText(path, text.Replace(oldText, newText));
    }

    // The staged vendor's own index (the one with machine models), not a library index added next to it.
    private static string VendorIndexPath(string leaf) =>
        Directory.EnumerateFiles(Path.Combine(leaf, "resources", "profiles"), "*.json", SearchOption.TopDirectoryOnly)
            .Single(file => File.ReadAllText(file).Contains("machine_model_list", StringComparison.Ordinal));

    private static void RemoveFromIndex(string leaf, string listKey, string presetName)
    {
        var path = VendorIndexPath(leaf);
        var index = JsonNode.Parse(File.ReadAllText(path))!;
        var list = index[listKey]!.AsArray();
        list.Remove(list.Single(entry => entry!["name"]!.GetValue<string>() == presetName));
        File.WriteAllText(path, index.ToJsonString());
    }

    private static void AddToIndex(string leaf, string listKey, string presetName, string subPath)
    {
        var path = VendorIndexPath(leaf);
        var index = JsonNode.Parse(File.ReadAllText(path))!;
        index[listKey]!.AsArray().Add(new JsonObject { ["name"] = presetName, ["sub_path"] = subPath });
        File.WriteAllText(path, index.ToJsonString());
    }

    /// <summary>
    /// Copies one or more of this project's linked VendorIndex fixture folders into a fresh temp
    /// directory shaped like a real bundle leaf (<c>&lt;root&gt;/orcaslicer/&lt;version&gt;/linux/resources/profiles/…</c>,
    /// matching <see cref="OpenFarm.Infrastructure.Parts.BundledSlicerScanner"/>'s own convention),
    /// optionally rewriting each fixture's vendor index top-level <c>"name"</c> to a unique value for
    /// test isolation (machine model names / setting_ids / preset content are left untouched). Every
    /// filament fixture's own <c>filament_vendor</c> value(s) are also suffixed with
    /// <paramref name="filamentVendorSuffix"/> — filament rows key off that resolved per-preset value now
    /// (research.md §0), not the renamed vendor index name above, so it needs its own isolation knob.
    /// </summary>
    private static string StageLeaf(string filamentVendorSuffix, params (string FixtureFolder, string? RenamedVendorName)[] vendors) =>
        StageLeafAt("1.0.0", filamentVendorSuffix, vendors);

    // The leaf directory name is the catalog version the rows are ingested under.
    private static string StageLeafAt(string version, string filamentVendorSuffix, params (string FixtureFolder, string? RenamedVendorName)[] vendors)
    {
        var engineSlug = vendors.Any(v => v.FixtureFolder == "BambuStudio") ? "bambustudio" : "orcaslicer";
        var leaf = Path.Combine(Path.GetTempPath(), "openfarm-ingestion-tests", Guid.NewGuid().ToString(), engineSlug, version, "linux");
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

        MakeFilamentVendorNamesTestUnique(profilesDir, filamentVendorSuffix);

        return leaf;
    }

    // Suffixes whatever brand a fixture's own filament_vendor already declares (e.g. "Generic" ->
    // "Generic-<suffix>", "Bambu Lab" -> "Bambu Lab-<suffix>") rather than collapsing every filament
    // preset onto one identical name — that would defeat the very resolution behavior under test, since
    // "Generic" and "Bambu Lab" need to stay distinguishable from each other while still being unique to
    // this staged leaf.
    private static void MakeFilamentVendorNamesTestUnique(string profilesDir, string filamentVendorSuffix)
    {
        foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(file);
            var rewritten = Regex.Replace(
                json,
                "\"filament_vendor\"\\s*:\\s*\\[\\s*\"([^\"]*)\"\\s*\\]",
                m => $"\"filament_vendor\": [\"{m.Groups[1].Value}-{filamentVendorSuffix}\"]");

            if (rewritten != json)
            {
                File.WriteAllText(file, rewritten);
            }
        }
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
