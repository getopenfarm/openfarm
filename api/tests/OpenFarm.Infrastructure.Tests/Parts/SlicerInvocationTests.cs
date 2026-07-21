using System.Text.Json;
using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class SlicerInvocationTests
{
    private static readonly AvailableSlicer Orca = new(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/bundle/orcaslicer/2.2.0/linux/AppRun");
    private static readonly AvailableSlicer SuperSlicerBinary = new(SlicerEngine.SuperSlicer, "2.5.59.13", SlicerPlatform.Linux, "/bundle/superslicer/2.5.59.13/linux/AppRun");

    [Fact]
    public void PrusaSliceFamilyInvocation_SupportsOrcaSlicerAndBambuStudioOnly()
    {
        var invocation = new PrusaSliceFamilyInvocation();

        invocation.SupportsEngine(SlicerEngine.OrcaSlicer).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.BambuStudio).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.SuperSlicer).Should().BeFalse();
    }

    [Fact]
    public void PrusaSliceFamilyInvocation_BuildInvocation_ExportsAGCodeBearingThreeMfViaGeneratedSettingsJson()
    {
        var workDirectory = Directory.CreateTempSubdirectory("slicer-invocation-test-");
        try
        {
            var invocation = new PrusaSliceFamilyInvocation();

            var result = invocation.BuildInvocation(Orca, "/tmp/model.3mf", "PETG", 0.4m, workDirectory.FullName);

            result.FileName.Should().Be(Orca.ExecutablePath);
            result.ProducesGCodeBearingThreeMf.Should().BeTrue();
            result.OutputPath.Should().Be(Path.Combine(workDirectory.FullName, "output.gcode.3mf"));

            var machineSettingsPath = Path.Combine(workDirectory.FullName, "machine_settings.json");
            var filamentSettingsPath = Path.Combine(workDirectory.FullName, "filament_settings.json");

            result.Arguments.Should().Be(
                $"--slice 0 --load-settings \"{machineSettingsPath}\" --load-filaments \"{filamentSettingsPath}\" " +
                $"--export-3mf \"{result.OutputPath}\" \"/tmp/model.3mf\"");

            File.Exists(machineSettingsPath).Should().BeTrue();
            using (var doc = JsonDocument.Parse(File.ReadAllText(machineSettingsPath)))
            {
                doc.RootElement.GetProperty("nozzle_diameter")[0].GetString().Should().Be("0.4");
            }

            File.Exists(filamentSettingsPath).Should().BeTrue();
            using (var doc = JsonDocument.Parse(File.ReadAllText(filamentSettingsPath)))
            {
                doc.RootElement.GetProperty("filament_type")[0].GetString().Should().Be("PETG");
            }
        }
        finally
        {
            workDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SuperSlicerInvocation_SupportsSuperSlicerOnly()
    {
        var invocation = new SuperSlicerInvocation();

        invocation.SupportsEngine(SlicerEngine.SuperSlicer).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.OrcaSlicer).Should().BeFalse();
        invocation.SupportsEngine(SlicerEngine.BambuStudio).Should().BeFalse();
    }

    [Fact]
    public void SuperSlicerInvocation_BuildInvocation_ProducesItsDivergentArgumentList()
    {
        var invocation = new SuperSlicerInvocation();

        var result = invocation.BuildInvocation(SuperSlicerBinary, "/tmp/model.3mf", "Generic PLA", 0.4m, "/tmp");

        result.FileName.Should().Be(SuperSlicerBinary.ExecutablePath);
        result.ProducesGCodeBearingThreeMf.Should().BeFalse();
        result.OutputPath.Should().Be(Path.Combine("/tmp", "output.gcode"));
        result.Arguments.Should().Be(
            "--slice --filament-type \"Generic PLA\" --nozzle-diameter 0.4 --export-gcode \"/tmp/output.gcode\" \"/tmp/model.3mf\"");
    }
}
