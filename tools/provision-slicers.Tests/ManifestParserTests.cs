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
              "schemaVersion": 2,
              "slicers": [
                { "engine": "OrcaSlicer", "version": "2.2.0", "assets": {
                    "Linux": "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.2.0/OrcaSlicer_Linux.AppImage",
                    "Windows": "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.2.0/OrcaSlicer_Windows.zip"
                  } },
                { "engine": "SuperSlicer", "version": "2.5.59.13", "assets": {
                    "Linux": "https://github.com/supermerill/SuperSlicer/releases/download/2.5.59.13/SuperSlicer.AppImage"
                  }, "notes": "pinned" }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeTrue();
        result.Manifest!.SchemaVersion.Should().Be(2);
        result.Manifest.Slicers.Should().HaveCount(2);
        result.Manifest.Slicers[0].Engine.Should().Be(Engine.OrcaSlicer);
        result.Manifest.Slicers[0].Assets.Should().BeEquivalentTo(new Dictionary<Platform, string>
        {
            [Platform.Linux] = "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.2.0/OrcaSlicer_Linux.AppImage",
            [Platform.Windows] = "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.2.0/OrcaSlicer_Windows.zip",
        });
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
                { "engine": "Cura", "version": "5.0.0", "assets": { "Linux": "https://example.test/cura.AppImage" } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Cura"));
    }

    [Fact]
    public void Parse_EmptyAssetsObject_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "version": "2.2.0", "assets": {} }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("assets"));
    }

    [Fact]
    public void Parse_UnknownPlatformKeyInAssets_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "version": "2.2.0", "assets": { "FreeBsd": "https://example.test/x" } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FreeBsd"));
    }

    [Fact]
    public void Parse_AssetValueNotAnAbsoluteUrl_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "version": "2.2.0", "assets": { "Linux": "not-a-url" } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("absolute http(s) URL"));
    }

    [Fact]
    public void Parse_EmptySlicersArray_ReportsError()
    {
        const string json = """{ "slicers": [] }""";

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
    }
}
