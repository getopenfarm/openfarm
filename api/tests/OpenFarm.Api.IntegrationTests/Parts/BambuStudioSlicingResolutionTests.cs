using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.IntegrationTests.Parts;

// quickstart Story 3 step 1, research.md §0a — a printer whose machine/process are Bambu-Studio-
// sourced must resolve settings JSON that actually contains the `include`-folded-in gcode fields
// (machine_start_gcode/change_filament_gcode/etc.), not silently drop them the way a plain
// ancestor-chain merge (with no `include` awareness) would.
public class BambuStudioSlicingResolutionTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Profiles", "Fixtures", "VendorIndex");

    [Fact]
    public async Task ResolveAsync_PrinterWithBambuStudioSourcedMachineAndProcess_ResolvesIncludedGcodeFields()
    {
        var vendorName = $"Bambulab-{Guid.NewGuid():N}";
        var leaf = StageBambuStudioLeaf(vendorName);
        using var scope = factory.Services.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        await ingestionService.IngestAsync(leaf, CancellationToken.None);

        var machineRepo = scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>();
        var processRepo = scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>();
        var printerRepo = scope.ServiceProvider.GetRequiredService<IPrinterRepository>();

        var machine = (await machineRepo.ListAsync(CancellationToken.None))
            .Single(p => p.VendorName == vendorName && p.ExternalId == "GM001");
        var process = (await processRepo.ListAsync(CancellationToken.None))
            .Single(p => p.VendorName == vendorName && p.ExternalId == "GP004");

        var printer = Printer.Create(
            "Bambu Studio Resolution Test Printer",
            "Bambu", "X1C", "10.0.0.96", $"BS-{Guid.NewGuid():N}");
        printer.LinkMachineProfile(machine.Id);
        printer.LinkProcessProfile(process.Id);
        // 012 — required for ResolveForPrinterAsync to succeed; the ingested BambuStudio PLA fixture
        // declares a real (non-"0") hot_plate_temp.
        printer.SelectBuildPlate(BuildPlateType.HighTemp);
        await printerRepo.AddAsync(printer, CancellationToken.None);
        await printerRepo.SaveChangesAsync(CancellationToken.None);

        var resolver = scope.ServiceProvider.GetRequiredService<ResolveSlicingProfileSelection>();

        var filament = (await scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>().ListAsync(default)).First(p => p.SourceFormat == OpenFarm.Domain.Profiles.ProfileSourceFormat.BambuStudio && p.ExternalId == "GFSA00" && p.IsSelectable);
        var result = await resolver.ResolveProfilesAsync(machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, new SlicerRef(SlicerEngine.BambuStudio, "1.0.0"), null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("machine_start_gcode");
        result.MachineSettingsJson.Should().Contain("change_filament_gcode");
    }

    // Two Bambu Studio versions ingested from the same vendor data, the second with edited presets. A printer keeps
    // pointing at the first version's rows; slicing for the second version must flatten the second version's data —
    // through real Postgres queries (the identity lookup and the ingested-versions check are SQL, which fakes cannot prove).
    [Fact]
    public async Task ResolveProfilesAsync_RowsPickedInAnOlderVersion_FlattenWithTheRunningVersionsData()
    {
        var vendor = $"Bambulab-{Guid.NewGuid():N}";
        var suffix = Guid.NewGuid().ToString("N");
        var v1 = StageBambuStudioLeaf(vendor, "3.0.0", suffix);
        var v2 = StageBambuStudioLeaf(vendor, "4.0.0", suffix);
        EditLeafFile(v2, "machine/Bambu Lab X1 Carbon 0.4 nozzle.json", "\"type\": \"machine\"", "\"type\": \"machine\", \"x_marker\": \"MACHINE-IN-4.0.0\"");
        // One of the gcode fragments the machine pulls in through `include`: resolved inside the running version.
        EditLeafFile(v2, "machine/Bambu Lab X1 Carbon 0.4 nozzle template machine_start_gcode.json", "\";===== machine: X1-0.4", "\";GCODE-IN-4.0.0 ===== machine: X1-0.4");
        EditLeafFile(v2, "process/0.20mm Standard @BBL X1C.json", "\"type\": \"process\"", "\"type\": \"process\", \"x_marker\": \"PROCESS-IN-4.0.0\"");
        EditLeafFile(v2, "filament/Bambu PLA Basic @base.json", "\"type\": \"filament\"", "\"type\": \"filament\", \"x_base_marker\": \"BASE-IN-4.0.0\"");
        EditLeafFile(v2, "filament/Bambu PLA Basic @BBL X1C.json", "\"type\": \"filament\"", "\"type\": \"filament\", \"x_marker\": \"LEAF-IN-4.0.0\"");

        using var scope = factory.Services.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
        await ingestion.IngestAsync(v1, CancellationToken.None);
        await ingestion.IngestAsync(v2, CancellationToken.None);

        var machine = (await scope.ServiceProvider.GetRequiredService<IMachineProfileRepository>().ListAsync(default))
            .Single(p => p.VendorName == vendor && p.ExternalId == "GM001" && p.SourceVersion == "3.0.0");
        var process = (await scope.ServiceProvider.GetRequiredService<IProcessProfileRepository>().ListAsync(default))
            .Single(p => p.VendorName == vendor && p.ExternalId == "GP004" && p.SourceVersion == "3.0.0");
        var filament = (await scope.ServiceProvider.GetRequiredService<IFilamentProfileRepository>().ListAsync(default))
            .Single(p => p.VendorName == $"Bambu Lab-{suffix}" && p.ExternalId == "GFSA00" && p.SourceVersion == "3.0.0");
        var resolver = scope.ServiceProvider.GetRequiredService<ResolveSlicingProfileSelection>();

        var asStored = await resolver.ResolveProfilesAsync(
            machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, new SlicerRef(SlicerEngine.BambuStudio, "3.0.0"), null, CancellationToken.None);
        var forVersion4 = await resolver.ResolveProfilesAsync(
            machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, new SlicerRef(SlicerEngine.BambuStudio, "4.0.0"), null, CancellationToken.None);
        var forUnknownVersion = () => resolver.ResolveProfilesAsync(
            machine.Id, process.Id, filament.Id, BuildPlateType.HighTemp, new SlicerRef(SlicerEngine.BambuStudio, "9.9.9"), null, CancellationToken.None);

        asStored.MachineSettingsJson.Should().NotContain("IN-4.0.0");
        asStored.FilamentSettingsJson.Should().NotContain("IN-4.0.0");
        forVersion4.MachineSettingsJson.Should().Contain("MACHINE-IN-4.0.0").And.Contain("GCODE-IN-4.0.0");
        forVersion4.ProcessSettingsJson.Should().Contain("PROCESS-IN-4.0.0");
        // The edited leaf AND the edited base it inherits from: the whole chain is the running version's.
        forVersion4.FilamentSettingsJson.Should().Contain("LEAF-IN-4.0.0").And.Contain("BASE-IN-4.0.0");
        // A catalog version that was never ingested is a reason to stop: the stored rows are not a stand-in.
        var blocked = await forUnknownVersion.Should().ThrowAsync<ProfileSetBlockedException>();
        blocked.Which.Code.Should().Be(nameof(ProfileResolutionOutcome.CatalogNotLoaded));
        blocked.Which.Message.Should().Contain("Bambu Studio 9.9.9 profiles are not loaded");
    }

    // The leaf directory name is the catalog version the rows are ingested under; a filament-vendor suffix keeps
    // filament rows from colliding with other tests' rows of the same version in this class's shared database.
    private static string StageBambuStudioLeaf(string renamedVendorName, string version = "1.0.0", string? filamentVendorSuffix = null)
    {
        var leaf = Path.Combine(Path.GetTempPath(), "openfarm-slicing-resolution-tests", Guid.NewGuid().ToString(), "bambustudio", version, "linux");
        var profilesDir = Path.Combine(leaf, "resources", "profiles");
        Directory.CreateDirectory(profilesDir);

        CopyDirectoryContents(Path.Combine(FixturesRoot, "BambuStudio"), profilesDir);
        RenameVendorIndexName(profilesDir, renamedVendorName);
        if (filamentVendorSuffix is not null)
        {
            foreach (var file in Directory.EnumerateFiles(profilesDir, "*.json", SearchOption.AllDirectories))
            {
                var json = File.ReadAllText(file);
                var rewritten = System.Text.RegularExpressions.Regex.Replace(
                    json, "\"filament_vendor\"\\s*:\\s*\\[\\s*\"([^\"]*)\"\\s*\\]",
                    m => $"\"filament_vendor\": [\"{m.Groups[1].Value}-{filamentVendorSuffix}\"]");
                if (rewritten != json)
                {
                    File.WriteAllText(file, rewritten);
                }
            }
        }

        return leaf;
    }

    private static void EditLeafFile(string leaf, string relativePath, string oldText, string newText)
    {
        var path = Path.Combine(leaf, "resources", "profiles", relativePath);
        var text = File.ReadAllText(path);
        text.Should().Contain(oldText);
        File.WriteAllText(path, text.Replace(oldText, newText));
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

            var currentName = document.RootElement.GetProperty("name").GetString();
            if (currentName is null)
            {
                continue;
            }

            var rewritten = json.Replace($"\"name\": \"{currentName}\"", $"\"name\": \"{newVendorName}\"");
            File.WriteAllText(file, rewritten);
        }
    }
}
