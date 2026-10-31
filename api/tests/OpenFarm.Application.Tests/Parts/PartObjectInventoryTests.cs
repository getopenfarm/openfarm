using System.IO.Compression;
using System.Text;
using FluentAssertions;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.Tests.Parts;

public class PartObjectInventoryTests
{
    private static PartObjectInventory Code(string code)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(code));
        return PartObjectInventory.Read(stream, "test.gcode", 1);
    }

    private static PartObjectInventory Archive(int plate, params (string Path, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        buffer.Position = 0;
        return PartObjectInventory.Read(buffer, "test.3mf", plate);
    }

    [Fact]
    public void EmptySettings_FallsThroughToSelectedSlicePlate()
    {
        var result = Archive(2, ("Metadata/model_settings.config", "<config><plate/></config>"),
            ("Metadata/slice_info.config", "<config><plate><metadata key='index' value='1'/><object/></plate><plate><metadata key='index' value='2'/><object/><object/><object/><object/></plate></config>"));
        result.Count.Should().Be(4);
        result.Source.Should().Be("SliceObjects");
        result.Objects.Should().BeEmpty("count metadata alone does not prove skip identities");
    }

    [Fact]
    public void NativeLabels_WinOverRepeatedDisplayNames()
    {
        var result = Code("; printing object same id:0 copy 0\n; start printing object, unique label id: 10\nM624 AQ==\n; printing object same id:0 copy 0\n; start printing object, unique label id: 20\nM624 Ag==\n; start printing object, unique label id: 10\nM624 AQ==\n");
        result.Count.Should().Be(2);
        result.Objects.Select(o => o.Id).Should().Equal("10", "20");
    }

    [Fact]
    public void RepeatedSourceAndWrapperObjects_AreIndependentInstances()
    {
        var result = Archive(1, ("Metadata/model_settings.config", "<config><plate><model_instance><metadata key='object_id' value='2'/><metadata key='identify_id' value='10'/></model_instance><model_instance><metadata key='object_id' value='4'/><metadata key='identify_id' value='20'/></model_instance></plate></config>"));
        result.Count.Should().Be(2);
        result.Objects.Should().HaveCount(2);
        result.SkipUnavailableReason.Should().BeNull();
    }

    [Fact]
    public void DuplicateNativeIds_DoNotEnableSkipping()
    {
        var result = Archive(1, ("Metadata/model_settings.config", "<config><plate><model_instance><metadata key='identify_id' value='10'/></model_instance><model_instance><metadata key='identify_id' value='10'/></model_instance></plate></config>"));
        result.Count.Should().Be(2);
        result.Objects.Should().BeEmpty();
        result.SkipUnavailableReason.Should().NotBeNull();
    }

    [Fact]
    public void BuildCountsInstances_AndIgnoresNonprintableItemsAndComponents()
    {
        var result = Archive(1, ("3D/3dmodel.model", "<model xmlns='http://schemas.microsoft.com/3dmanufacturing/core/2015/02'><resources><object id='1'/><object id='2'/></resources><build><item objectid='1'/><item objectid='1'/><item objectid='2' printable='0'/></build></model>"));
        result.Count.Should().Be(2);
        result.Objects.Should().BeEmpty();
    }

    [Fact]
    public void ConflictingDefinitions_DoNotEnableSkipping()
    {
        var result = Code("EXCLUDE_OBJECT_DEFINE NAME=A\nEXCLUDE_OBJECT_DEFINE NAME=B\nEXCLUDE_OBJECT_START NAME=A\n");
        result.SkipUnavailableReason.Should().Contain("disagree");
    }

    [Fact]
    public void DisplayNamesAlone_AreUnknown_NotAProvenSingleton()
    {
        Code("; printing object same id:0 copy 0\n").Count.Should().BeNull();
        Code("; object_count = 1\n").Count.Should().Be(1);
        Code("; object_count = 1\n").Objects.Should().BeEmpty();
    }

    [Fact]
    public void OneUnsupportedIdentifier_InvalidatesTheWholeMapping()
    {
        var result = Code("EXCLUDE_OBJECT_START NAME=A\nEXCLUDE_OBJECT_START NAME=bad$name\n");
        result.Objects.Should().BeEmpty();
        result.SkipUnavailableReason.Should().Contain("unsupported");
    }

    [Fact]
    public void BuildPrintability_AlsoFiltersSelectedModelInstances()
    {
        var result = Archive(1,
            ("Metadata/model_settings.config", "<config><plate><model_instance><metadata key='object_id' value='2'/><metadata key='identify_id' value='10'/></model_instance><model_instance><metadata key='object_id' value='4'/><metadata key='identify_id' value='20'/></model_instance></plate></config>"),
            ("3D/3dmodel.model", "<model><build><item objectid='2'/><item objectid='4' printable='0'/></build></model>"));
        result.Count.Should().Be(1);
        result.Objects.Select(o => o.Id).Should().Equal("10");
    }
}
