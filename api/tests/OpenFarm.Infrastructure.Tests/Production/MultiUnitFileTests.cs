using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Printers.Bambu;
using OpenFarm.Infrastructure.Production;

namespace OpenFarm.Infrastructure.Tests.Production;

public class MultiUnitFileTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Parts", "Fixtures", "P1SMultiUnit", name));

    [Theory]
    [InlineData("clip_bambu_studio.3mf", "112,123,134,156", true)]
    [InlineData("clip_bambu_studio_sliced.gcode.3mf", "112,123,134,156", true)]
    [InlineData("clip_orca.3mf", "106,117,128,150", true)]
    [InlineData("clip_orca_sliced.gcode.3mf", "106,117,128,150", true)]
    [InlineData("clip_orca.gcode", "106,117,128,150", false)]
    public void RealExports_CountEveryInstance_RegardlessOfNameOrGeometry(string name, string ids, bool skippable)
    {
        var bytes = Fixture(name);
        using var source = new MemoryStream(bytes);
        SlicedFileUnitsPerPrint.Suggest(source, name, 1).Should().Be(4);
        var manifest = PrintObjectManifestReader.Read(bytes, "revision", 1, 1); // Stale capacity must never collapse four objects into one unit.
        manifest.DetectedCount.Should().Be(4);
        manifest.Objects.Select(o => o.Id).Should().BeEquivalentTo(ids.Split(','));
        if (skippable)
        {
            manifest.Units.Should().HaveCount(4);
            manifest.Units.Should().OnlyContain(u => u.ObjectIds.Count == 1);
            manifest.Validate(4);
            var act = () => manifest.Validate(1);
            act.Should().Throw<InvalidOperationException>();
        }
        else
        {
            manifest.Units.Should().BeEmpty();
            manifest.SkipUnavailableReason.Should().Contain("sliced 3MF");
        }
    }

    [Theory]
    [InlineData("clip_bambu_studio")]
    [InlineData("clip_orca")]
    public void MatchingSourceAndSlice_KeepNativeIdentities(string prefix)
    {
        var source = PrintObjectManifestReader.Read(Fixture(prefix + ".3mf"), "revision", 1, 4);
        var execution = new PrintObjectExecution(source, [source.Units[0].Id], [], []);
        var sliced = Fixture(prefix + "_sliced.gcode.3mf");
        ObjectExecutionArtifact.Prepare(sliced, 1, execution).Should().Equal(sliced);
        execution.PlannedExclusions.Should().HaveCount(3);
    }

    [Fact]
    public void DifferentExporterIdentities_CannotBeSubstituted()
    {
        var source = PrintObjectManifestReader.Read(Fixture("clip_bambu_studio.3mf"), "revision", 1, 4);
        var execution = new PrintObjectExecution(source, ["1"], [], []);
        var act = () => ObjectExecutionArtifact.Prepare(Fixture("clip_orca_sliced.gcode.3mf"), 1, execution);
        act.Should().Throw<InvalidOperationException>().WithMessage("*identities changed*");
    }

    [Fact]
    public void RawAndEmbeddedCode_HaveEqualCounts_ButWrappingDoesNotProveSkipIdentities()
    {
        var raw = Fixture("clip_orca.gcode");
        using var archive = new ZipArchive(new MemoryStream(Fixture("clip_orca_sliced.gcode.3mf")));
        using var embedded = archive.GetEntry("Metadata/plate_1.gcode")!.Open();
        using var buffer = new MemoryStream();
        embedded.CopyTo(buffer);
        buffer.ToArray().Should().Equal(raw);
        var wrapped = BambuPrintFile.PrepareProject(raw, "raw.gcode", 1);
        var manifest = PrintObjectManifestReader.Read(wrapped, "revision", 1, 4);
        manifest.DetectedCount.Should().Be(4);
        manifest.Units.Should().BeEmpty();
        manifest.SkipUnavailableReason.Should().Contain("identity metadata");
        var slicedManifest = PrintObjectManifestReader.Read(Fixture("clip_orca_sliced.gcode.3mf"), "revision", 1, 4);
        var act = () => ObjectExecutionArtifact.Prepare(wrapped, 1, new(slicedManifest, ["1"], [], []));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UnknownCount_IsNotAConfirmedSingleton()
    {
        var manifest = PrintObjectManifestReader.Read(Encoding.UTF8.GetBytes("; printing object repeated name\n"), "revision", 1, 1);
        manifest.DetectedCount.Should().BeNull();
        manifest.Units.Should().BeEmpty();
    }

    [Fact]
    public void LegacyGroupedExecution_RemainsReadable_ButCannotBePreparedForNewDispatch()
    {
        var sliced = Fixture("clip_orca_sliced.gcode.3mf");
        var manifest = PrintObjectManifestReader.Read(sliced, "revision", 1, 4);
        var grouped = manifest with { Units = [new("1", manifest.Objects.Select(o => o.Id).ToArray())] };
        var execution = new PrintObjectExecution(grouped, ["1"], [], [], Observed: true);
        PrintObjectExecution.Read(execution.ToJson())!.GoodUnits.Should().Be(1, "historical accounting retains its original unit meaning");
        var act = () => ObjectExecutionArtifact.Prepare(sliced, 1, execution);
        act.Should().Throw<InvalidOperationException>().WithMessage("*obsolete*");
    }
}
