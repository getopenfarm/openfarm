using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class SlicerInvocationTests
{
    private static readonly AvailableSlicer Orca = new(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/bundle/orcaslicer/2.2.0/linux/AppRun");
    private static readonly AvailableSlicer SuperSlicerBinary = new(SlicerEngine.SuperSlicer, "2.5.59.13", SlicerPlatform.Linux, "/bundle/superslicer/2.5.59.13/linux/AppRun");

    private static readonly ResolvedSlicingProfiles ResolvedProfiles = new(
        MachineSettingsJson: """{"type":"machine","name":"Test Machine","nozzle_diameter":["0.4"]}""",
        ProcessSettingsJson: """{"type":"process","name":"0.20mm Standard","layer_height":"0.2"}""",
        FilamentSettingsJson: """{"type":"filament","name":"Generic PETG","filament_type":["PETG"]}""",
        MachineProfileName: "Test Machine",
        ProcessProfileName: "0.20mm Standard",
        FilamentProfileName: "Generic PETG");

    [Fact]
    public void PrusaSliceFamilyInvocation_SupportsOrcaSlicerAndBambuStudioOnly()
    {
        var invocation = new PrusaSliceFamilyInvocation();

        invocation.SupportsEngine(SlicerEngine.OrcaSlicer).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.BambuStudio).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.SuperSlicer).Should().BeFalse();
    }

    [Fact]
    public void PrusaSliceFamilyInvocation_BuildInvocation_WritesResolvedProfilesAndExportsAGCodeBearingThreeMf()
    {
        var workDirectory = Directory.CreateTempSubdirectory("slicer-invocation-test-");
        try
        {
            var invocation = new PrusaSliceFamilyInvocation();

            var result = invocation.BuildInvocation(Orca, "/tmp/model.3mf", "PETG", 0.4m, workDirectory.FullName, ResolvedProfiles);

            result.FileName.Should().Be(Orca.ExecutablePath);
            result.ProducesGCodeBearingThreeMf.Should().BeTrue();
            result.OutputPath.Should().Be(Path.Combine(workDirectory.FullName, "output.gcode.3mf"));

            var machineSettingsPath = Path.Combine(workDirectory.FullName, "machine_settings.json");
            var processSettingsPath = Path.Combine(workDirectory.FullName, "process_settings.json");
            var filamentSettingsPath = Path.Combine(workDirectory.FullName, "filament_settings.json");

            result.Arguments.Should().Be(
                $"--slice 0 --load-settings \"{machineSettingsPath};{processSettingsPath}\" --load-filaments \"{filamentSettingsPath}\" " +
                "--nozzle-diameter 0.4 --filament-type \"PETG\" " +
                $"--export-3mf \"{result.OutputPath}\" \"/tmp/model.3mf\"");

            File.ReadAllText(machineSettingsPath).Should().Be(ResolvedProfiles.MachineSettingsJson);
            File.ReadAllText(processSettingsPath).Should().Be(ResolvedProfiles.ProcessSettingsJson);
            File.ReadAllText(filamentSettingsPath).Should().Be(ResolvedProfiles.FilamentSettingsJson);
        }
        finally
        {
            workDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PrusaSliceFamilyInvocation_BuildInvocation_NullResolvedProfiles_ThrowsSlicingFailedException()
    {
        var workDirectory = Directory.CreateTempSubdirectory("slicer-invocation-test-");
        try
        {
            var invocation = new PrusaSliceFamilyInvocation();

            var act = () => invocation.BuildInvocation(Orca, "/tmp/model.3mf", "PETG", 0.4m, workDirectory.FullName, null);

            act.Should().Throw<SlicingFailedException>();
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

        var result = invocation.BuildInvocation(SuperSlicerBinary, "/tmp/model.3mf", "Generic PLA", 0.4m, "/tmp", resolvedProfiles: null);

        result.FileName.Should().Be(SuperSlicerBinary.ExecutablePath);
        result.ProducesGCodeBearingThreeMf.Should().BeFalse();
        result.OutputPath.Should().Be(Path.Combine("/tmp", "output.gcode"));
        result.Arguments.Should().Be(
            "--slice --filament-type \"Generic PLA\" --nozzle-diameter 0.4 --export-gcode \"/tmp/output.gcode\" \"/tmp/model.3mf\"");
    }

    [Fact]
    public void SuperSlicerInvocation_BuildInvocation_IgnoresANonNullResolvedProfiles()
    {
        var invocation = new SuperSlicerInvocation();

        var result = invocation.BuildInvocation(SuperSlicerBinary, "/tmp/model.3mf", "Generic PLA", 0.4m, "/tmp", ResolvedProfiles);

        result.Arguments.Should().Be(
            "--slice --filament-type \"Generic PLA\" --nozzle-diameter 0.4 --export-gcode \"/tmp/output.gcode\" \"/tmp/model.3mf\"");
    }
}
