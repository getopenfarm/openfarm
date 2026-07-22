using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Organizations;
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
            Organization.WellKnownId, "Bambu Studio Resolution Test Printer",
            "Bambu", "X1C", "10.0.0.96", $"BS-{Guid.NewGuid():N}");
        printer.LinkMachineProfile(machine.Id);
        printer.LinkProcessProfile(process.Id);
        await printerRepo.AddAsync(printer, CancellationToken.None);
        await printerRepo.SaveChangesAsync(CancellationToken.None);

        var resolver = scope.ServiceProvider.GetRequiredService<ResolveSlicingProfileSelection>();

        var result = await resolver.ResolveAsync(printer.Id, "PLA", narrowedFilamentProfileId: null, CancellationToken.None);

        result.MachineSettingsJson.Should().Contain("machine_start_gcode");
        result.MachineSettingsJson.Should().Contain("change_filament_gcode");
    }

    private static string StageBambuStudioLeaf(string renamedVendorName)
    {
        var leaf = Path.Combine(Path.GetTempPath(), "openfarm-slicing-resolution-tests", Guid.NewGuid().ToString(), "bambustudio", "1.0.0", "linux");
        var profilesDir = Path.Combine(leaf, "resources", "profiles");
        Directory.CreateDirectory(profilesDir);

        CopyDirectoryContents(Path.Combine(FixturesRoot, "BambuStudio"), profilesDir);
        RenameVendorIndexName(profilesDir, renamedVendorName);

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
