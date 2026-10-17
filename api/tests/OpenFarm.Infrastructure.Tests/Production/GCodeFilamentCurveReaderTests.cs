using FluentAssertions;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

public class GCodeFilamentCurveReaderTests
{
    private static IReadOnlyDictionary<int, double[]> Read(string gcode) => GCodeFilamentCurveReader.Read(new StringReader(gcode));

    [Fact]
    public void Read_AttributesExtrusionToTheSelectedToolPerLayer_IgnoringStartUpPurge()
    {
        const string gcode = """
            M83
            T1000
            G1 E50 F200
            T0
            ; layer num/total_layer_count: 1/3
            G1 X1 Y1 E1.5 F1800
            G1 X2 Y1 E1.5
            ; layer num/total_layer_count: 2/3
            T1
            G1 E4 F300 ; flush into the new colour
            G1 X3 Y1 E2
            ; layer num/total_layer_count: 3/3
            G1 X4 Y1 E2
            """;

        var curves = Read(gcode);

        curves[0].Should().Equal(3, 3, 3);
        curves[1].Should().Equal(0, 6, 8);
    }

    [Fact]
    public void Read_FirstFilamentComesFromTheStartGCodeAmsSwitch_NotAssumedToBeTool0()
    {
        const string gcode = "M83\nM620 S2A   ; switch material if AMS exist\nM621 S2A\n; layer num/total_layer_count: 1/2\nG1 X1 E3\n"
            + "; layer num/total_layer_count: 2/2\nM620 S0A\nT0\nG1 X2 E4\n";

        var curves = Read(gcode);

        curves[2].Should().Equal(3, 3);
        curves[0].Should().Equal(0, 4);
    }

    [Fact]
    public void Read_RetractAndUnretractCancel()
    {
        const string gcode = """
            M83
            T0
            ; layer num/total_layer_count: 1/1
            G1 X1 E2
            G1 E-0.8
            G1 X9 Y9
            G1 E0.8
            G1 X2 E2
            """;

        Read(gcode)[0].Should().Equal(4);
    }

    [Fact]
    public void Read_AbsoluteExtrusionUsesDeltasAndHonoursG92Resets()
    {
        const string gcode = """
            M82
            T0
            ; layer num/total_layer_count: 1/2
            G92 E0
            G1 X1 E1
            G1 X2 E3
            G92 E0
            ; layer num/total_layer_count: 2/2
            G1 X3 E2
            """;

        Read(gcode)[0].Should().Equal(3, 5);
    }

    [Fact]
    public void Read_NonBambuLayerChangeMarkers_AreCounted()
    {
        const string gcode = "M83\nT0\n;LAYER_CHANGE\nG1 E1\n;LAYER_CHANGE\nG1 E2\n";

        Read(gcode)[0].Should().Equal(1, 3);
    }

    [Fact]
    public void Read_ToolThatNeverExtrudes_IsOmitted_AndNoLayersMeansNothing()
    {
        Read("M83\nT0\n; layer num/total_layer_count: 1/1\nG1 X1\nT2\n").Should().BeEmpty();
        Read("G1 E5").Should().BeEmpty();
    }

    /// <summary>Real OrcaSlicer 2.3.1 output (Bambu P1S): the curve ends within 2% of the slicer's own `filament used [mm]`.</summary>
    [Fact]
    public void Read_RealOrcaBenchy_MatchesSlicerReportedFilamentLength()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "RealSlicer", "orcaslicer-2.3.1-benchy-3mf", "3DBenchy_PLA_35m31s.gcode");
        using var reader = new StreamReader(path);

        var curves = GCodeFilamentCurveReader.Read(reader);

        curves.Keys.Should().Equal(0);
        curves[0].Should().HaveCount(240);
        curves[0][^1].Should().BeApproximately(3658.25, 3658.25 * 0.02);
        curves[0].Zip(curves[0].Skip(1)).Should().OnlyContain(p => p.Second >= p.First);
    }
}
