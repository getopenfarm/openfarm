using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

public class ObjectExecutionArtifactTests
{
    [Theory]
    [InlineData(0, 26)]
    [InlineData(2, 46)]
    public void FilamentAccounting_PreservesPurgeSharedMovesAndExtrusionBeforeALiveSkip(int skipLayer, double expectedMm)
    {
        const string code = "M83\nG1 E5\n;LAYER_CHANGE\nEXCLUDE_OBJECT_START NAME=A\nG1 E10\nEXCLUDE_OBJECT_END NAME=A\nEXCLUDE_OBJECT_START NAME=B\nG1 E20\nEXCLUDE_OBJECT_END NAME=B\nG1 E3\n;LAYER_CHANGE\nEXCLUDE_OBJECT_START NAME=A\nG1 E10\nEXCLUDE_OBJECT_END NAME=A\nEXCLUDE_OBJECT_START NAME=B\nG1 E20\nEXCLUDE_OBJECT_END NAME=B\nG1 E3\n";
        var extrusion = GCodeFilamentCurveReader.Read(new StringReader(code), new Dictionary<string, OpenFarm.Domain.Production.ProgressMark> { ["B"] = new(skipLayer, -1) });
        extrusion.Tools[0].PrimeMm.Should().Be(5);
        extrusion.Tools[0].CumulativeMm.Last().Should().Be(expectedMm);
    }

    [Fact]
    public void Exclusions_AreInsertedAfterStartupAndReset_BeforeTheFirstObject()
    {
        const string code = "START_PRINT\nEXCLUDE_OBJECT_DEFINE RESET=1\nEXCLUDE_OBJECT_DEFINE NAME=A\nEXCLUDE_OBJECT_START NAME=A\nG1 X1 E1\nEXCLUDE_OBJECT_END NAME=A\n";
        var prepared = ObjectExecutionArtifact.PrepareKlipper(code, ["A"]);
        prepared.Should().Contain("EXCLUDE_OBJECT_DEFINE NAME=A\nEXCLUDE_OBJECT NAME=A\nEXCLUDE_OBJECT_START NAME=A");
        prepared.Should().StartWith("START_PRINT\nEXCLUDE_OBJECT_DEFINE RESET=1");
    }

    [Theory]
    [InlineData("A\nCANCEL_PRINT")]
    [InlineData("A;CANCEL_PRINT")]
    public void ObjectNames_CannotInjectCommands(string id)
    {
        var act = () => ObjectExecutionArtifact.PrepareKlipper("EXCLUDE_OBJECT_START NAME=A\n", [id]);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResetInsideObjectRegions_IsRejected()
    {
        var act = () => ObjectExecutionArtifact.PrepareKlipper("EXCLUDE_OBJECT_START NAME=A\nEXCLUDE_OBJECT_DEFINE RESET=1\n", ["A"]);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NativeLabels_CreateOneUnitPerObject()
    {
        var manifest = PrintObjectManifestReader.Read(Encoding.UTF8.GetBytes("EXCLUDE_OBJECT_START NAME=A\nEXCLUDE_OBJECT_END NAME=A\nEXCLUDE_OBJECT_START NAME=B\n"), "hash", 1, 2);
        manifest.Objects.Should().HaveCount(2);
        manifest.Units.Should().HaveCount(2);
        manifest.Units.Should().OnlyContain(u => u.ObjectIds.Count == 1);
    }

    [Fact]
    public void OnlyTheSelectedPlateContributesObjectIdentities()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var one = new StreamWriter(archive.CreateEntry("Metadata/plate_1.gcode").Open());
            one.Write("EXCLUDE_OBJECT_START NAME=A\n");
            one.Dispose();
            using var two = new StreamWriter(archive.CreateEntry("Metadata/plate_2.gcode").Open());
            two.Write("EXCLUDE_OBJECT_START NAME=B\n");
        }
        var manifest = PrintObjectManifestReader.Read(buffer.ToArray(), "hash", 2, 1);
        manifest.Objects.Select(o => o.Id).Should().Equal("B");
    }
    [Fact]
    public void OrcaMissingInstanceMetadata_CreatesCompleteUnitsForEveryVerifiedPrintedObject()
    {
        var ids = Enumerable.Range(344, 20).ToArray();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var info = new StreamWriter(archive.CreateEntry("Metadata/slice_info.config").Open()))
                info.Write("<config><plate><metadata key='index' value='2'/><object identify_id='344' name='Handle cover'/></plate></config>");
            using var code = new StreamWriter(archive.CreateEntry("Metadata/plate_2.gcode").Open());
            code.Write($"; model label id: {string.Join(',', ids)}\n");
            foreach (var id in ids.Reverse())
                code.Write($"; start printing object, unique label id: {id}\nM624 {Convert.ToBase64String(BitConverter.GetBytes(1UL << (id - 344)))}\n");
        }
        var manifest = PrintObjectManifestReader.Read(buffer.ToArray(), "hash", 2, 20);
        manifest.Validate(20);
        manifest.DetectedCount.Should().Be(20);
        manifest.Objects.Should().HaveCount(20);
        manifest.Units.Should().HaveCount(20);
        manifest.Units.SelectMany(unit => unit.ObjectIds).Should().BeEquivalentTo(ids.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var execution = new OpenFarm.Domain.Production.PrintObjectExecution(manifest,
            manifest.Units.Take(3).Select(unit => unit.Id).ToArray(), [], []);
        execution.PlannedExclusions.Should().HaveCount(17);
    }

}
