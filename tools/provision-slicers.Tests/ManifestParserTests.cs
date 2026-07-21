using FluentAssertions;
using OpenFarm.Tools.ProvisionSlicers;

namespace OpenFarm.Tools.ProvisionSlicers.Tests;

public class ManifestParserTests
{
    [Fact]
    public void Parse_ValidManifest_ReturnsParsedEntries()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "slicers": [
                { "engine": "OrcaSlicer", "version": "2.2.0", "platforms": ["Linux", "Windows"] },
                { "engine": "SuperSlicer", "version": "2.5.59.13", "platforms": ["Linux"], "notes": "pinned" }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeTrue();
        result.Manifest!.SchemaVersion.Should().Be(1);
        result.Manifest.Slicers.Should().HaveCount(2);
        result.Manifest.Slicers[0].Engine.Should().Be(Engine.OrcaSlicer);
        result.Manifest.Slicers[0].Platforms.Should().BeEquivalentTo([Platform.Linux, Platform.Windows]);
        result.Manifest.Slicers[1].Notes.Should().Be("pinned");
    }

    [Fact]
    public void Parse_MissingRequiredFields_ReportsErrorsAndNoManifest()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer" }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Manifest.Should().BeNull();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_UnknownEngineValue_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "Cura", "version": "5.0.0", "platforms": ["Linux"] }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Cura"));
    }

    [Fact]
    public void Parse_EmptyPlatformsArray_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "version": "2.2.0", "platforms": [] }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("platforms"));
    }

    [Fact]
    public void Parse_EmptySlicersArray_ReportsError()
    {
        const string json = """{ "slicers": [] }""";

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
    }
}
