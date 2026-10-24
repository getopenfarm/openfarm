using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;
using OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

namespace OpenFarm.Infrastructure.Tests.Parts;

// Production multi-colour slicing against the real CLI: a project's filament slots, each with its own line's preset and
// colour, slice as that many filaments — and the output keeps the slot colours, which is how dispatch matches the file's
// tools back to job lines. The project is made here by the same CLI (two cubes, one per filament), so no binary fixture.
// Measured before per-slot files: with one --load-filaments file the CLI sliced slot 1 with it and every other slot with
// the preset embedded in the project — unresolved against the part's pin — which is why each slot now gets its own file.
public class RealSlicerMultiColourTests
{
    private static readonly SlicerRef Orca231 = new(SlicerEngine.OrcaSlicer, "2.3.1");

    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1")]
    public async Task OrcaSlicer231_TwoColourProject_OnAnAmsPrinter_SlicesBothSlotsWithTheirOwnFilament()
    {
        var gcode = await SliceTwoColourProjectAsync("Bambu Lab P1S 0.4 nozzle", "0.20mm Standard @BBL X1C",
            "Bambu PLA Basic @BBL X1C", "Bambu PLA Matte @BBL X1C", BuildPlateType.TexturedPei, perSlotFiles: true);

        AssertBothSlotsPrint(gcode);
        // One nozzle, two filaments: every colour change purges, so the flush matrix must hold real volumes.
        Numbers(gcode, "flush_volumes_matrix").Should().Contain(volume => volume > 0);
    }

    [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1")]
    public async Task OrcaSlicer231_TwoColourProject_OnTheSnapmakerU1_PrintsEachSlotOnItsOwnTool()
    {
        var gcode = await SliceTwoColourProjectAsync("Snapmaker U1 (0.4 nozzle)", "0.20 Standard @Snapmaker U1 (0.4 nozzle)",
            "Snapmaker PLA @U1", "Snapmaker PLA Matte @U1", BuildPlateType.HighTemp, perSlotFiles: true);

        AssertBothSlotsPrint(gcode);
    }

    private static void AssertBothSlotsPrint(string gcode)
    {
        var values = GCodeMetadataHelper.ParseComments(gcode);
        values["filament_colour"].Should().Be("#FF0000;#0000FF", "each slot keeps its own colour");
        Numbers(gcode, "filament used [g]").Should().HaveCount(2).And.OnlyContain(grams => grams > 0, "both cubes print");
        Tools(gcode).Should().Contain(["T0", "T1"]);
    }

    private static async Task<string> SliceTwoColourProjectAsync(string machineName, string processName, string firstFilament,
        string secondFilament, BuildPlateType plate, bool perSlotFiles)
    {
        var (available, reason) = RealSlicerBundleLocator.TryResolve(Orca231.Engine, Orca231.Version);
        available.Should().NotBeNull(reason);
        var catalogs = await VendorProfileFixtureStaging.StageCatalogsAsync([Orca231], CancellationToken.None);
        var machine = await catalogs.MachineAsync(Orca231, machineName);
        var process = await catalogs.ProcessAsync(Orca231, processName);
        var first = await catalogs.Resolver.ResolveProfilesAsync(machine.Id, process.Id, (await catalogs.FilamentAsync(Orca231, firstFilament)).Id,
            plate, Orca231, use: null, CancellationToken.None);
        var second = await catalogs.Resolver.ResolveProfilesAsync(machine.Id, process.Id, (await catalogs.FilamentAsync(Orca231, secondFilament)).Id,
            plate, Orca231, use: null, CancellationToken.None);

        var project = await TwoCubeProjectAsync(available!, first, second);
        var profiles = first with
        {
            Filaments = [new(first.FilamentSettingsJson, first.Filament), new(second.FilamentSettingsJson, second.Filament)],
            Slots = perSlotFiles ? [new SlotFilament(0, "#FF0000"), new SlotFilament(1, "#0000FF")] : [],
        };
        return await RealSlicerProcessRunner.SliceAsync(available!, new PrusaSliceFamilyInvocation(), project, PartFileFormat.ThreeMf,
            "PLA", 0.4m, profiles, CancellationToken.None);
    }

    /// <summary>An unsliced project with two cubes, the first on filament 1 (red), the second on filament 2 (blue).</summary>
    private static async Task<byte[]> TwoCubeProjectAsync(AvailableSlicer slicer, ResolvedSlicingProfiles first, ResolvedSlicingProfiles second)
    {
        var work = Directory.CreateTempSubdirectory("openfarm-two-cube-project-");
        try
        {
            string Write(string name, string content)
            {
                var path = Path.Combine(work.FullName, name);
                File.WriteAllText(path, content);
                return path;
            }

            var machine = Write("machine.json", first.MachineSettingsJson);
            var process = Write("process.json", first.ProcessSettingsJson);
            var red = Write("red.json", WithColour(first.FilamentSettingsJson, "#FF0000"));
            var blue = Write("blue.json", WithColour(second.FilamentSettingsJson, "#0000FF"));
            var left = Write("left.stl", Cube(20));
            var right = Write("right.stl", Cube(20));
            var project = Path.Combine(work.FullName, "project.3mf");

            await RunAsync(slicer, work.FullName,
                $"--allow-newer-file --load-settings \"{machine};{process}\" --load-filaments \"{red};{blue}\" --load-filament-ids \"1,2\" " +
                $"--arrange 1 --export-3mf \"{project}\" \"{left}\" \"{right}\"");
            return await File.ReadAllBytesAsync(project);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static string WithColour(string filamentJson, string color)
    {
        var settings = JsonNode.Parse(filamentJson)!.AsObject();
        settings["filament_colour"] = new JsonArray(color);
        return settings.ToJsonString();
    }

    private static string Cube(double size)
    {
        double[][] corners =
        [
            [0, 0, 0], [size, 0, 0], [size, size, 0], [0, size, 0],
            [0, 0, size], [size, 0, size], [size, size, size], [0, size, size],
        ];
        int[][] faces =
        [
            [0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4],
            [1, 2, 6], [1, 6, 5], [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7],
        ];
        var stl = new StringBuilder("solid cube\n");
        foreach (var face in faces)
        {
            stl.Append("facet normal 0 0 0\nouter loop\n");
            foreach (var corner in face)
                stl.Append(CultureInfo.InvariantCulture, $"vertex {corners[corner][0]} {corners[corner][1]} {corners[corner][2]}\n");
            stl.Append("endloop\nendfacet\n");
        }

        return stl.Append("endsolid cube\n").ToString();
    }

    private static async Task RunAsync(AvailableSlicer slicer, string workDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(slicer.ExecutablePath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workDirectory,
        })!;
        var stderr = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));
        process.ExitCode.Should().Be(0, await stderr);
    }

    private static IEnumerable<string> Tools(string gcode) =>
        gcode.Split('\n').Select(line => line.Trim()).Where(line => line.Length is >= 2 and <= 3 && line[0] == 'T' && char.IsAsciiDigit(line[1])).Distinct();

    private static List<decimal> Numbers(string gcode, string key) =>
        (GCodeMetadataHelper.ParseComments(gcode).GetValueOrDefault(key) ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => decimal.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();
}
