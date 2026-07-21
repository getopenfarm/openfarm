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
    public void PrusaSliceFamilyInvocation_BuildInvocation_ProducesExpectedArgumentList()
    {
        var invocation = new PrusaSliceFamilyInvocation();

        var result = invocation.BuildInvocation(Orca, "/tmp/model.3mf", "Generic PLA", 0.4m, "/tmp/output.gcode");

        result.FileName.Should().Be(Orca.ExecutablePath);
        result.Arguments.Should().Be(
            "--slice 0 --load-filaments \"Generic PLA\" --nozzle-diameter 0.4 --export-gcode \"/tmp/output.gcode\" \"/tmp/model.3mf\"");
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

        var result = invocation.BuildInvocation(SuperSlicerBinary, "/tmp/model.3mf", "Generic PLA", 0.4m, "/tmp/output.gcode");

        result.FileName.Should().Be(SuperSlicerBinary.ExecutablePath);
        result.Arguments.Should().Be(
            "--slice --filament-type \"Generic PLA\" --nozzle-diameter 0.4 --export-gcode \"/tmp/output.gcode\" \"/tmp/model.3mf\"");
    }
}
