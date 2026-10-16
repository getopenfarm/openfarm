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
              "schemaVersion": 4,
              "slicers": [
                { "engine": "OrcaSlicer", "builds": {
                    "Linux": [ { "version": "2.2.0", "assetUrl": "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.2.0/OrcaSlicer_Linux.AppImage" } ],
                    "Windows": [ { "version": "2.3.1", "assetUrl": "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.3.1/OrcaSlicer_Windows.zip", "notes": "win pin" } ]
                  } },
                { "engine": "SuperSlicer", "builds": {
                    "Linux": [ { "version": "2.5.59.13", "assetUrl": "https://github.com/supermerill/SuperSlicer/releases/download/2.5.59.13/SuperSlicer.AppImage" } ]
                  }, "notes": "pinned" }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeTrue();
        result.Manifest!.SchemaVersion.Should().Be(4);
        result.Manifest.Slicers.Should().HaveCount(2);
        result.Manifest.Slicers[0].Engine.Should().Be(Engine.OrcaSlicer);
        result.Manifest.Slicers[0].Builds[Platform.Linux].Should().Equal(
            new PlatformBuild("2.2.0", "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.2.0/OrcaSlicer_Linux.AppImage"));
        result.Manifest.Slicers[0].Builds[Platform.Windows].Should().Equal(
            new PlatformBuild("2.3.1", "https://github.com/SoftFever/OrcaSlicer/releases/download/v2.3.1/OrcaSlicer_Windows.zip", "win pin"));
        result.Manifest.Slicers[1].Notes.Should().Be("pinned");
    }

    [Fact]
    public void Parse_DifferentVersionSetsPerPlatform_KeepsEachPlatformsOwnList()
    {
        const string json = """
            {
              "schemaVersion": 4,
              "slicers": [
                { "engine": "OrcaSlicer", "builds": {
                    "MacOs": [ { "version": "2.3.1", "assetUrl": "https://example.test/mac-2.3.1.dmg" } ],
                    "Linux": [
                      { "version": "2.4.2", "assetUrl": "https://example.test/linux-2.4.2.AppImage" },
                      { "version": "2.3.1", "assetUrl": "https://example.test/linux-2.3.1.AppImage" }
                    ]
                  } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeTrue();
        result.Manifest!.Slicers[0].Builds[Platform.MacOs].Select(b => b.Version).Should().Equal("2.3.1");
        result.Manifest.Slicers[0].Builds[Platform.Linux].Select(b => b.Version).Should().Equal("2.4.2", "2.3.1");
    }

    [Fact]
    public void Parse_DuplicateVersionWithinPlatform_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "Linux": [
                    { "version": "2.3.1", "assetUrl": "https://example.test/a.AppImage" },
                    { "version": "2.3.1", "assetUrl": "https://example.test/b.AppImage" }
                  ] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("builds['Linux'][1].version") && e.Contains("more than once"));
    }

    [Fact]
    public void Parse_EngineDeclaredTwice_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "Linux": [ { "version": "2.4.2", "assetUrl": "https://example.test/a.AppImage" } ] } },
                { "engine": "OrcaSlicer", "builds": { "Linux": [ { "version": "2.3.1", "assetUrl": "https://example.test/b.AppImage" } ] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("slicers[1].engine") && e.Contains("already declared"));
    }

    [Fact]
    public void Parse_SchemaVersion3_ReportsUnsupported()
    {
        const string json = """
            {
              "schemaVersion": 3,
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "Linux": { "version": "2.2.0", "assetUrl": "https://example.test/x.AppImage" } } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unsupported schemaVersion 3"));
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
                { "engine": "Cura", "builds": { "Linux": [ { "version": "5.0.0", "assetUrl": "https://example.test/cura.AppImage" } ] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Cura"));
    }

    [Fact]
    public void Parse_EmptyBuildsObject_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": {} }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("builds"));
    }

    [Fact]
    public void Parse_EmptyPlatformBuildList_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "Linux": [] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("non-empty array"));
    }

    [Fact]
    public void Parse_UnknownPlatformKeyInBuilds_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "FreeBsd": [ { "version": "2.2.0", "assetUrl": "https://example.test/x" } ] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FreeBsd"));
    }

    [Fact]
    public void Parse_BuildAssetUrlNotAnAbsoluteUrl_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "Linux": [ { "version": "2.2.0", "assetUrl": "not-a-url" } ] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("absolute http(s) URL"));
    }

    [Fact]
    public void Parse_BuildMissingVersion_ReportsError()
    {
        const string json = """
            {
              "slicers": [
                { "engine": "OrcaSlicer", "builds": { "Linux": [ { "assetUrl": "https://example.test/x.AppImage" } ] } }
              ]
            }
            """;

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("version"));
    }

    [Fact]
    public void Parse_EmptySlicersArray_ReportsError()
    {
        const string json = """{ "slicers": [] }""";

        var result = ManifestParser.Parse(json);

        result.Success.Should().BeFalse();
    }
}
